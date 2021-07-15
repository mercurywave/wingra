using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	public enum eStaticType { Data, Function, Constant, External, Library, Root, EnumType, EnumValue }
	public class StaticMapping
	{
		TreeNode _root = new TreeNode(eStaticType.Root);
		Dictionary<string, TreeNode> _fileRefs = new Dictionary<string, TreeNode>(); // uses file keys
		Dictionary<string, string> _globals = new Dictionary<string, string>(); // glo => fileKey
		Map<string, string> _scratches = new Map<string, string>(); // glo => fileKeys
		class TreeNode
		{
			public Dictionary<string, TreeNode> Children = new Dictionary<string, TreeNode>();
			public eStaticType Type;
			public string SourceFileKey;
			public int FileLine = -1;
			public _SfunctionDef FuncDef;
			public SExpressionComponent Value;
			public TreeNode(eStaticType type, string sourceFile = "", int fileLine = -1, _SfunctionDef funcDef = null, SExpressionComponent value = null)
			{
				Replace(type, sourceFile, fileLine, funcDef, value);
			}
			internal void Replace(eStaticType type, string sourceFile = "", int fileLine = -1, _SfunctionDef funcDef = null, SExpressionComponent value = null)
			{
				Type = type;
				SourceFileKey = sourceFile;
				FileLine = fileLine;
				FuncDef = funcDef;
				Value = value;
			}
			public TreeNode GetMakeChild(string child, eStaticType type, string sourceFile, int fileLine, _SfunctionDef funcDef = null, SExpressionComponent value = null)
			{
				if (!Children.ContainsKey(child))
					Children.Add(child, new TreeNode(type, sourceFile, fileLine, funcDef, value));
				else
					Children[child].Replace(type, sourceFile, fileLine, funcDef, value);
				return Children[child];
			}
			public TreeNode TryGetChild(string child)
			{
				if (!Children.ContainsKey(child))
					return null;
				return Children[child];
			}
			public bool HasChild(string child)
				=> Children.ContainsKey(child);

			public TreeNode MockCopy()
			{
				TreeNode copy = new TreeNode(Type, SourceFileKey, FileLine);
				foreach (var pair in Children)
					copy.Children.Add(pair.Key, pair.Value.MockCopy());
				return copy;
			}
		}

		public StaticMapping CloneForExport()
		{
			// this is super-specifically to support mocking functions that will be injected during boot
			// all the alternatives are really super gross
			// compile once with all symbols, then compile again with this copy
			var copy = new StaticMapping();
			foreach (var pair in _fileRefs)
				copy._fileRefs.Add(pair.Key, pair.Value.MockCopy());
			copy._root = _root.MockCopy();
			return copy;
		}

		TreeNode GetForFile(string key)
		{
			if (!_fileRefs.ContainsKey(key))
				_fileRefs.Add(key, new TreeNode(eStaticType.Root, key));
			return _fileRefs[key];
		}

		internal void AddStaticGlobal(string path, eStaticType type, string sourceFile, int fileLine, _SfunctionDef funcDef = null, SExpressionComponent value = null)
		{
			var arr = util.Split(path, ".");
			var node = _root;
			MakeBranch(sourceFile, node, arr, fileLine, type, funcDef, value);
		}
		internal void AddFilePath(string fileKey, string path, eStaticType type, int fileLine, _SfunctionDef funcDef = null, SExpressionComponent value = null)
		{
			var arr = util.Split(path, ".");
			var node = GetForFile(fileKey);
			MakeBranch(fileKey, node, arr, fileLine, type, funcDef, value);
		}
		// helper for adding end points
		void MakeBranch(string fileKey, TreeNode root, string[] path, int fileLine, eStaticType type, _SfunctionDef funcDef = null, SExpressionComponent value = null)
		{
			for (int i = 0; i < path.Length - 1; i++) // stops 1 short
				root = root.GetMakeChild(path[i], eStaticType.Library, fileKey, fileLine);
			root.GetMakeChild(path[path.Length - 1], type, fileKey, fileLine, funcDef, value);
		}


		public const string DATA = "DATA";
		public const string FILE = "FILE";
		public const string DATA_ABS = "D";
		public const string FILE_ABS = "F";
		// fileKey is blank for things like on-demand compilation where there is no file
		public bool TryResolveAbsolutePath(string fileKey, List<string> possiblePrefixes, string[] path, out List<string> matches, out string[] dynamicPath)
		{
			matches = new List<string>();
			var success = _TryResolveAbsolutePath(fileKey, possiblePrefixes, path, out var hash, out dynamicPath);
			if (!success) return false;
			foreach (var node in hash)
				matches.Add(node.Key);
			return true;
		}
		List<string> ExpandPrefixes(string fileKey, List<string> prefixes)
		{
			var file = GetForFile(fileKey);
			var data = _root;
			Map<string, TreeNode> nodes = new Map<string, TreeNode>();
			var chained = new List<string>();

			void AddNode(string p, TreeNode n)
			{
				chained.Add(p);
				nodes.Add(p, n);
			}

			// prefixes are in the oposite order from the file
			for (int i = prefixes.Count - 1; i >= 0; i--)
			{
				var path = prefixes[i];
				for (int j = chained.Count - 1; j >= 0; j--)
				{
					var pre = chained[j];
					if (!nodes.Exists(pre)) continue;
					var splt = SplitPath(path);
					foreach (var poss in nodes.Values(pre))
						if (HasPath(poss, splt, out var next))
							AddNode(JoinPath(CombinePaths(pre, path)), next);
				}
				var split = SplitPath(path);

				TreeNode target;
				if (HasPath(file, split, out target))
					AddNode(path, target);
				if (HasPath(data, split, out target))
					AddNode(path, target);

			}
			return chained;
		}
		bool _TryResolveAbsolutePath(string fileKey, List<string> possiblePrefixes, string[] path, out Dictionary<string, TreeNode> matches, out string[] dynamicPath)
		{
			matches = new Dictionary<string, TreeNode>();
			if (path.Length == 0) { dynamicPath = new string[0]; return false; }
			var file = new List<string[]>();
			var data = new List<string[]>();
			string[] dynPath = new string[0];
			if (path[0] == DATA)
				data.Add(util.RangeRemainder(path, 1));
			else if (path[0] == FILE)
				file.Add(util.RangeRemainder(path, 1));
			else
			{
				file.Add(path);
				if (possiblePrefixes != null && possiblePrefixes.Count > 0)
					foreach (var poss in ExpandPrefixes(fileKey, possiblePrefixes))
						file.Add(CombinePaths(poss, path));
				data = file; // we'll check all against both global and file.
							 // PERF: ExpandPrefixes knows what the path is, we could optimize this
			}

			if (fileKey != "")
				CheckTree(FILE_ABS, GetForFile(fileKey), file, matches, fileKey);
			CheckTree(DATA_ABS, _root, data, matches, "");

			void CheckTree(string type, TreeNode node, List<string[]> toCheck, Dictionary<string, TreeNode> addTo, string fkey)
			{
				foreach (var poss in toCheck)
				{
					if (HasPath(node, poss, out var target, out var staticPath, out var dPath))
					{
						var key = type + "|" + util.Join(staticPath, ".") + "|" + fkey;
						if (!addTo.ContainsKey(key))
							addTo.Add(key, target);
						dynPath = dPath;
					}
				}
			}
			dynamicPath = dynPath;
			return (matches.Count == 1);
		}

		internal void ReserveNamespace(string fileKey, string prefix, string path, eStaticType type, int fileLine, _SfunctionDef funcDef = null, SExpressionComponent value = null)
		{
			if (prefix == FILE)
				AddFilePath(fileKey, path, type, fileLine, funcDef, value);
			else if (prefix == DATA)
				AddStaticGlobal(path, type, fileKey, fileLine, funcDef, value);
		}

		internal void ReserveNamespace(string fileKey, int fileLine, RelativeTokenReference[] writtenPath, string declaringPath, eStaticType type, _SfunctionDef funcDef = null, SExpressionComponent value = null)
		{
			ResolveNamespace(fileKey, fileLine, writtenPath, declaringPath, out var prefix, out var path);
			ReserveNamespace(fileKey, prefix, path, type, fileLine, funcDef, value);
		}

		public void ResolveNamespace(string fileKey, int fileLine, RelativeTokenReference[] writtenPath, string declaringPath, out string prefix, out string path)
		{
			var direct = writtenPath.Select(t => t.Token.Token.Replace("$", "")).ToArray();
			path = util.Join(direct, ".");
			if (declaringPath == null)
			{
				prefix = FILE;
			}
			else
			{
				if (declaringPath != "")
				{
					var arr = SplitPath(declaringPath, out prefix);
					if (prefix != FILE && prefix != DATA)
						throw new NotImplementedException("I missed a scenario");
					if (arr.Length > 0)
						path = JoinPath(arr) + "." + path;
				}
				else
					prefix = DATA;
			}
		}

		public string ResolvePath(string fileKey, int fileLine, RelativeTokenReference[] writtenPath, List<string> usingPrefixes, out string prefix, out string path, out string resolvedFile, out string[] dynamicPath)
		{
			var fullPath = ResolvePath(fileKey, fileLine, writtenPath, usingPrefixes, out dynamicPath);
			var arr = util.Split(fullPath, "|");
			prefix = arr[0];
			path = arr[1];
			resolvedFile = arr[2];
			return fullPath;
		}
		string ResolvePath(string fileKey, int fileLine, RelativeTokenReference[] writtenPath, List<string> usingPrefixes, out string[] dynamicPath)
		{
			var direct = writtenPath.Select(t => t.Token.Token.Replace("$", "")).ToArray();
			if (TryResolveAbsolutePath(fileKey, usingPrefixes, direct, out var matches, out dynamicPath))
				return matches[0];
			else
			{
				if (matches.Count == 0)
					throw new CompilerException("Could not resolve static reference - " + JoinPath(writtenPath), fileLine, writtenPath[0]);
				else
					throw new CompilerException("Ambiguous static reference. matches: " + util.Join(matches, ", "), fileLine, writtenPath[0]);
			}
		}

		// these may return the closest node in the case we can't resolve the complete path now (data subnodes)
		bool HasPath(TreeNode node, string[] path, string[] addPaths, out TreeNode target, out string[] staticPath, out string[] dynamicPath)
			=> HasPath(node, CombinePaths(path, addPaths), out target, out staticPath, out dynamicPath);
		bool HasPath(TreeNode node, string[] path, out TreeNode target)
			=> HasPath(node, path, out target, out _, out _);
		bool HasPath(TreeNode node, string[] path, out TreeNode target, out string[] staticPath, out string[] dynamicPath)
		{
			dynamicPath = new string[0];
			staticPath = util.RangeRemainder(path, 0);
			for (int i = 0; i < path.Length; i++)
			{
				var child = path[i];
				node = node.TryGetChild(child);
				if (node == null)
				{
					staticPath = new string[0];
					dynamicPath = new string[0];
					target = null;
					return false;
				}
				if (node.Type == eStaticType.Data || node.Type == eStaticType.EnumValue)
				{
					if (i < path.Length - 1)
					{
						staticPath = util.RangeFront(path, i + 1);
						dynamicPath = util.RangeRemainder(path, i + 1);
					}
					break;
				}
			}
			target = node;
			return true;
		}
		TreeNode GetAbsNode(string absPath)
		{
			var path = SplitAbsPath(absPath, out var prefix, out var fileKey);
			var node = (prefix == DATA_ABS) ? _root : GetForFile(fileKey);
			if (!HasPath(node, path, out var target)) return null;
			return target;
		}

		public static string GetPathFromAbsPath(string abs) => util.Piece(abs, "|", 2);
		public static string[] SplitPath(string path) => util.Split(path, ".").ToArray();
		public static string[] SplitPath(string path, out string prefix)
			=> SplitAbsPath(SplitPath(path), out prefix);
		public static string[] SplitAbsPath(string path) => util.Split(path, "|").ToArray();
		public static string[] SplitAbsPath(string path, out string prefix, out string fileKey)
		{
			var arr = util.Split(path, "|");
			prefix = arr[0];
			fileKey = arr[2];
			return util.Split(arr[1], ".");
		}
		public static string[] SplitAbsPath(string[] path, out string prefix)
		{
			prefix = path[0];
			return util.RangeRemainder(path, 1);
		}
		public static string[] CombinePaths(string path, string additional)
		{
			var a = util.Split(path, ".").ToArray();
			var b = util.Split(additional, ".").ToArray();
			return CombinePaths(a, b);
		}
		public static string[] CombinePaths(string path, string[] additional)
		{
			var arr = util.Split(path, ".").ToArray();
			return CombinePaths(arr, additional);
		}
		public static string[] CombinePaths(string[] path, string additional)
		{
			var arr = util.Split(additional, ".").ToArray();
			return CombinePaths(path, arr);
		}
		public static string[] CombinePaths(string[] path, string[] additional)
		{
			return path.Concat(additional).ToArray();
		}

		public static string JoinPath(RelativeTokenReference[] path)
			=> JoinPath(path.Select(t => t.Token.Token.Replace("$", "")).ToArray());
		public static string JoinPath(string[] path)
			=> util.Join(path, ".");



		public bool HasGlobal(string name)
			=> _globals.ContainsKey(name);

		public void TryRegisterScratch(string name, string fileKey, bool isGlobal)
		{
			if (isGlobal)
			{
				if (HasGlobal(name))
					throw new CompilerException("Global " + name + " is already declared in " + _globals[name], -1);
				_globals[name] = fileKey;
			}
			else
			{
				if (_scratches.Exists(name, fileKey))
					throw new CompilerException("Scratch " + name + " declared twice in same file", -1);
				_scratches.Add(name, fileKey);
			}
		}

		public bool TryResolveScratch(string name, string currFile, out string targetFile)
		{
			targetFile = ResolveScratch(name, currFile);
			return targetFile != null;
		}
		public string ResolveScratch(string name, string currFile)
		{
			if (_scratches.Exists(name, currFile))
				return currFile;
			if (_globals.ContainsKey(name))
				return _globals[name];
			return null;
		}

		public List<string> SuggestGlobals(string name, string fileKey)
		{
			name = name.ToLower();
			List<string> list = new List<string>();
			foreach (var glo in _globals.Keys)
				if (glo.ToLower().Contains(name))
					list.Add(glo);
			foreach (var scr in _scratches.Keys())
				if (_scratches.Exists(scr, fileKey))
					if (scr.ToLower().Contains(name))
						list.Add(scr);
			return list;
		}

		#region completion match and editor stuff

		public Dictionary<string, eStaticType> SuggestToken(string fileKey, string path, List<string> usingPrefixes)
		{
			var arr = SplitPath(path);
			TrySuggest(fileKey, usingPrefixes, arr, out var matches);
			return matches;
		}
		bool TrySuggest(string fileKey, List<string> possiblePrefixes, string[] path, out Dictionary<string, eStaticType> matches)
		{
			matches = new Dictionary<string, eStaticType>();
			if (path.Length == 0) return false;
			var file = new List<string[]>();
			var data = new List<string[]>();

			file.Add(path);
			if (possiblePrefixes != null && possiblePrefixes.Count > 0)
				foreach (var poss in ExpandPrefixes(fileKey, possiblePrefixes))
					file.Add(CombinePaths(poss, path));
			data = file; // we'll check all against both global and file

			if (fileKey != "")
				CheckTree(FILE, GetForFile(fileKey), file, matches);
			CheckTree(DATA, _root, data, matches);

			void CheckTree(string type, TreeNode node, List<string[]> toCheck, Dictionary<string, eStaticType> addTo)
			{
				foreach (var poss in toCheck)
				{
					var close = SuggestHasPath(node, poss);
					foreach (var hit in close)
						addTo.Add(type + "." + hit.Key, hit.Value);
				}
			}

			return (matches.Count == 1);
		}
		Dictionary<string, eStaticType> SuggestHasPath(TreeNode node, string[] path)
		{
			for (int i = 0; i < path.Length - 1; i++)
			{
				var child = path[i];
				node = node.TryGetChild(child);
				if (node == null) return new Dictionary<string, eStaticType>();
			}
			var last = path[path.Length - 1];
			var poss = new Dictionary<string, eStaticType>();
			var partial = JoinPath(util.RangeFront(path, path.Length - 1));
			foreach (var child in node.Children)
			{
				if (child.Key.ToLower().Contains(last.ToLower()))
				{
					if (partial == "")
						poss.Add(child.Key, child.Value.Type);
					else
						poss.Add(partial + "." + child.Key, child.Value.Type);
				}
			}
			return poss;
		}

		public List<string> SuggestAll(string fileKey, List<string> possiblePrefixes)
		{
			var matches = new List<string>();

			if (fileKey != "")
				CheckTree(FILE, GetForFile(fileKey), matches);
			CheckTree(DATA, _root, matches);

			void CheckTree(string type, TreeNode node, List<string> addTo)
			{
				foreach (var poss in possiblePrefixes)
				{
					var arr = util.Split(poss, ".");
					if (HasPath(node, arr, out var target))
					{
						foreach (var child in target.Children)
							addTo.Add(child.Key);
					}
				}
				foreach (var child in node.Children)
					addTo.Add(child.Key);
			}
			return matches;
		}

		public Tuple<string, int> GetJumpToTarget(string fileKey, string path, List<string> usingPrefixes)
		{
			var arr = SplitPath(path);
			var success = _TryResolveAbsolutePath(fileKey, usingPrefixes, arr, out var hash, out _);
			if (!success) return null;
			var node = hash.First().Value;
			if (node.SourceFileKey == "") return null;
			return new Tuple<string, int>(node.SourceFileKey, node.FileLine);
		}

		public void FlushFile(string key)
		{
			var file = GetForFile(key);
			file.Children.Clear();
			FlushFileRec(_root, key);
			foreach (var pair in _globals.ToArray())
				if (pair.Value == key)
					_globals.Remove(key);
			foreach (var pair in _scratches.Values().ToArray())
				if (pair.Value == key)
					_scratches.Kill(pair.Key, pair.Value);
		}
		bool FlushFileRec(TreeNode node, string key)
		{
			HashSet<string> tokill = new HashSet<string>();
			bool leavesRemain = false;
			foreach (var child in node.Children)
			{
				var branchStays = FlushFileRec(child.Value, key);
				leavesRemain = leavesRemain || branchStays || child.Value.SourceFileKey != key;
				if (child.Value.SourceFileKey != key) continue;
				if (branchStays) continue;
				tokill.Add(child.Key);
			}
			foreach (var dead in tokill)
				node.Children.Remove(dead);
			return leavesRemain;
		}

		public string GetAbsPath(string fileKey, string path, List<string> possiblePrefixes, out string[] dynamicPath)
		{
			var arr = SplitPath(path);
			if (!TryResolveAbsolutePath(fileKey, possiblePrefixes, arr, out var matches, out dynamicPath))
				return "";
			if (matches.Count != 1)
				return "";
			return matches[0];
		}
		public bool TryGetFunctionInfo(string asbPath, out string name, out bool isMethod, out string[] inputs, out string[] outputs, out bool doesYield, out bool isAsync)
		{
			var node = GetAbsNode(asbPath);
			name = ""; isMethod = false; inputs = null; outputs = null; isMethod = false; doesYield = false; isAsync = false;
			if (node == null) return false;
			var func = node.FuncDef;
			if (func == null) return false;
			name = func.Identifier;
			isMethod = func._isMethod;
			doesYield = func._doesYield;
			inputs = func.Parameters.Select(p => p.GetDisplayString()).ToArray();
			outputs = func._returnParams.Select(r => r.Symbol).ToArray();
			isAsync = func._isAsync;
			return true;
		}
		internal _SfunctionDef TryGetFunction(string absPath)
		{
			if (absPath == "") return null;
			// inputs are a bit awkward because that's what was convienent for the caller
			var node = GetAbsNode(absPath);
			if (node == null) return null;
			return node.FuncDef;
		}
		internal SExpressionComponent TryGetConstant(string absPath)
		{
			if (absPath == "") return null;
			// inputs are a bit awkward because that's what was convienent for the caller
			var node = GetAbsNode(absPath);
			if (node == null) return null;
			return node.Value;
		}
		public bool TryGetMainFunc(out bool isAsync)
		{
			return TryGetGlobalFunc("Main", out isAsync);
		}
		public bool TryGetTestFunc(out bool isAsync)
		{
			return TryGetGlobalFunc("TestMain", out isAsync);
		}

		private bool TryGetGlobalFunc(string name, out bool isAsync)
		{
			isAsync = false;
			var node = _root.TryGetChild(name);
			if (node == null)
				return false;
			if (node.Type != eStaticType.Function)
				return false;
			isAsync = node.FuncDef._isAsync;
			return true;
		}
		#endregion
	}
}
