using Wingra.Interpreter;
using Wingra.Parser;
using ILanguage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wingra
{
	public class WingraProject : ILanguage.CodeProject
	{
		const string EXTENSION = "wng";
		const string PROJ_EXTENSION = "wingraProj";

		SortedDictionary<string, WingraBuffer> _wingraFiles = new SortedDictionary<string, WingraBuffer>();
		SortedDictionary<string, WingraBuffer> _allWingraFiles = new SortedDictionary<string, WingraBuffer>();
		public Dictionary<string, string> Config;
		public List<string> RequiredPaths = new List<string>();
		public List<string> Extensions = new List<string>();
		public List<WingraProject> RequiredProjects = new List<WingraProject>();

		public Compiler IncrementalDebugCompiler; // managed by editor, if any
		public WingraProject(string path, IServeCodeFiles server, WingraProject stdLib)
			: base(path, server)
		{
			if (stdLib != null)
				RequiredProjects.Add(stdLib);
		}

		#region config file

		protected override void LoadConfigProject(ITextBuffer projectFile)
		{
			Config = new Dictionary<string, string>();
			if (projectFile == null) return;
			if (projectFile.Lines == 0) return;
			if (_CheckLine(projectFile.TextAtLine(0), "version", out var version))
			{
				if (version == "1")
				{
					for (int i = 1; i < projectFile.Lines; i++)
					{
						var text = projectFile.TextAtLine(i);
						string parm = "";
						if (_ReadRequirement(text, out parm))
							RequiredPaths.Add(parm);
						else if (_ReadExtension(text, out parm))
							Extensions.Add(parm);
						else if (_ReadLine(text, out var key, out var value))
							Config[key] = value;
					}
				}
			}
		}
		bool _CheckLine(string text, string key, out string value)
		{
			var result = _ReadLine(text, out var k, out value);
			return result && k.ToLower() == key.ToLower();
		}
		bool _ReadLine(string text, out string key, out string value)
		{
			key = ""; value = "";
			text = text.Trim();
			if (text == "") return false;
			if (text[0] == ';') return false;
			var idx = text.IndexOf('=');
			if (idx < 0) return false;
			key = util.BoundedSubstr(text, 0, idx).Trim();
			value = util.BoundedSubstr(text, idx + 1, text.Length).Trim();
			if (value == "" || value == "") return false;
			return true;
		}
		bool _ReadRequirement(string text, out string path)
			=> _ReadKey(ref text, out path, "requires");
		bool _ReadExtension(string text, out string path)
			=> _ReadKey(ref text, out path, "extension");

		private static bool _ReadKey(ref string text, out string path, string key)
		{
			path = "";
			text = text.Trim();
			if (text.Length >= key.Length && text.StartsWith(key + " "))
			{
				path = util.BoundedSubstr(text, key.Length + 1, text.Length - (key.Length + 1));
				path = path.Trim();
				return true;
			}
			return false;
		}

		public override string ProjExtension => PROJ_EXTENSION;

		public bool DoRunTests => CheckConfigFlag("runTests");
		public bool IsJsExport => CheckConfigString("jsExport") != "";
		public bool CheckConfigFlag(string key)
		{
			if (Config == null) return false;
			if (!Config.ContainsKey(key)) return false;
			return Config[key] == "1" || Config[key].ToLower() == "true";
		}
		public string CheckConfigString(string key, string fallback = "")
		{
			if (Config == null) return fallback;
			if (!Config.ContainsKey(key)) return fallback;
			return Config[key];
		}
		// for command line arguments
		public void SetConfigFlag(string key, bool value)
			=> Config?.Add(key, "1");

		#endregion

		public override void AddLoadedFile(string key)
		{
			base.AddLoadedFile(key);
			if (IsFileWingra(key))
				_wingraFiles.Add(key, null);
		}

		protected override async Task<ITextBuffer> _CreateNew(string key)
		{
			if (!IsFileWingra(key))
				return await base._CreateNew(key);
			var buff = new WingraBuffer(key);
			_wingraFiles.Add(key, buff);
			_allWingraFiles.Add(key, buff);
			return buff;
		}

		public WingraBuffer GetFile(string key) => _allWingraFiles[key];
		public bool IsFileLoaded(string key) => _allWingraFiles.ContainsKey(key);

		protected override async Task<ITextBuffer> _LoadByKey(string key)
		{
			if (!IsFileWingra(key))
				return await base._LoadByKey(key);
			var buffer = new WingraBuffer(key);
			await _fileServer.AsyncLoadFile(key, buffer);
			_wingraFiles[key] = buffer;
			buffer.Dirty = false;
			return buffer;
		}

		public override async Task AllFilesAdded()
		{
			// I didn't want to pre-load all these files for the editor, but I can't compile unless everything is loaded
			// maybe someday I can defer this
			await LoadAllFiles();
			foreach (var prj in GetProjectLoadOrder())
				foreach (var file in prj._wingraFiles)
					_allWingraFiles.Add(file.Key, file.Value);
			await base.AllFilesAdded();
		}

		internal async Task LoadAllFiles()
		{
			foreach (var pair in _wingraFiles.ToArray())
				if (pair.Value == null)
					await LoadFile(pair.Key);
		}

		public List<WingraProject> GetProjectLoadOrder()
		{
			List<WingraProject> list = new List<WingraProject>();
			ScanProjects(list, this);
			return list;
		}
		void ScanProjects(List<WingraProject> list, WingraProject prj)
		{
			foreach (var child in prj.RequiredProjects)
				if (!list.Contains(child))
					ScanProjects(list, child);
			list.Add(prj);
		}

		public static bool IsFileWingra(string file) => file.ToLower().EndsWith("." + EXTENSION);
		public static bool IsFileWingraProject(string file) => file.ToLower().EndsWith("." + PROJ_EXTENSION);
		public override string FileExtension => EXTENSION;

		internal Parser.ErrorList CompileErrors = new Parser.ErrorList();

		public bool CheckForErrors()
		{
			foreach (var prj in GetProjectLoadOrder())
				if (prj.CompileErrors.Errors.Any())
					return true;
			return false;
		}

		public ErrorLogger GetFileErrorLogger(WingraBuffer buff)
		{
			foreach (var prj in GetProjectLoadOrder())
				if (prj._wingraFiles.ContainsKey(buff.Key))
					return prj.CompileErrors.GetFileLogger(buff);
			return CompileErrors.GetFileLogger(buff);
		}
		public void ClearFileErrors(WingraBuffer buff)
			=> GetProjectLoadOrder().ForEach(prj => prj.CompileErrors.ClearForFile(buff));

		public List<SyntaxError> GetAllErrors()
			=> GetProjectLoadOrder().SelectMany(prj => prj.CompileErrors.Errors).ToList();

		public IEnumerable<WingraBuffer> IterAllFiles() => _wingraFiles.Select(p => p.Value);
		public IEnumerable<WingraBuffer> IterAllFilesRecursive() => GetProjectLoadOrder().SelectMany(p => p.IterAllFiles());

		public WingraCompile CompileAll(StaticMapping mapping, bool isDebug, bool isTest, bool isIDE, WingraSymbols symbols = null, Compiler compiler = null)
			=> CompileAll(new Compiler(mapping, isDebug, isTest, false, isIDE), symbols);

		public WingraCompile CompileAll(Compiler compiler, WingraSymbols symbols = null)
		{
			CompileErrors.Clear();
			var comp = new WingraCompile();

			foreach (var child in GetProjectLoadOrder())
				child._CompileAll(comp, compiler, symbols);

			comp.SortLoadOrder();
			return comp;
		}

		void _CompileAll(WingraCompile comp, Compiler compiler, WingraSymbols symbols = null)
		{
			var files = IterAllFiles().ToArray();
			foreach (var file in files)
			{
				compiler.StaticMap.FlushFile(file.Key);
				PreCompileFile(compiler, file);
			}
			compiler.Bootstrap(CompileErrors.GetLogger());
			STopOfFile[] parsed = new STopOfFile[files.Length];

			for (int i = 0; i < files.Length; i++)
				parsed[i] = ParseFile(compiler, files[i]);

			for (int i = 0; i < files.Length; i++)
				if (parsed[i] != null)
					CompileFile(compiler, files[i], parsed[i], comp, symbols);
		}

		void PreCompileFile(Compiler compiler, WingraBuffer file)
		{
			try
			{
				compiler.PreParse(file, CompileErrors.GetFileLogger(file));
			}
			catch (Exception e)
			{
				CompileErrors.LogError("EXCEPTION (pre-parse):" + e.Message + "\n" + e.StackTrace, file);
			}
		}

		STopOfFile ParseFile(Compiler compiler, WingraBuffer file)
		{
			try
			{
				var parse = compiler.Parse(file, CompileErrors.GetFileLogger(file));
				if (CompileErrors.Errors.Any()) return null;

				return parse;
			}
			catch (Exception e)
			{
				CompileErrors.LogError("EXCEPTION:" + e.Message + "\n" + e.StackTrace, file);
				return null;
			}
		}

		void CompileFile(Compiler compiler, WingraBuffer file, STopOfFile parse, WingraCompile comp, WingraSymbols symbols)
		{
			try
			{
				var assm = compiler.Compile(file.ShortFileName, file.Key, parse, CompileErrors.GetFileLogger(file));
				if (CompileErrors.Errors.Any()) return;

				if (symbols != null)
					symbols.Register(file, assm);
				if (comp != null)
					comp.Assemblies.Add(assm);
			}
			catch (Exception e)
			{
				CompileErrors.LogError("EXCEPTION:" + e.Message + "\n" + e.StackTrace, file);
			}
		}
	}
	public class WingraCompile
	{
		internal List<AssemblyFile> Assemblies = new List<AssemblyFile>();

		public StringBuilder ExportByteCode()
		{
			StringBuilder sb = new StringBuilder();
			foreach (var file in Assemblies)
			{
				sb.AppendLine("@FILE=" + file.Name);
				foreach (var pair in file.AllDebugCode())
				{
					sb.AppendLine("@" + pair.Key);
					foreach (var line in pair.Value)
					{
						var str = line.GetCommandShort();
						str += "\t" + line.Param;
						str += "\t" + line.FloatLiteral;
						str += "\t" + Escape(line.Literal);
						sb.AppendLine(str);
					}
				}
			}
			return sb;
		}
		static string Escape(string input)
			=> input.Replace("\t", "\\t")
				.Replace("\n", "\\n")
				.Replace("\r", "\\r");

		internal void SortLoadOrder()
		{
			var orig = Assemblies.ToList();
			Assemblies = SortFiles(orig);
		}
		static List<AssemblyFile> SortFiles(List<AssemblyFile> orig)
		{
			List<AssemblyFile> output = new List<AssemblyFile>();
			List<AssemblyFile> toAdd = orig.ToList();
			var required = new DualIndex<string, AssemblyFile>();
			var declares = new DualIndex<string, AssemblyFile>();

			foreach (var file in orig)
			{
				if (file.RequiredSymbols != null)
					foreach (var key in file.RequiredSymbols)
						required.Set(key, file);
				if (file.ExportedSymbols != null)
					foreach (var key in file.ExportedSymbols)
						declares.Set(key, file);
			}

			// If a file depends on itself, remove that requirement
			// I guess I could have these happen last for that requirement, but that's gross
			foreach (var key in declares.Keys())
				foreach (var file in declares.Values(key))
					if (required.Contains(key, file))
						required.Kill(key, file);

			while (toAdd.Count > 0)
			{
				int prevCount = toAdd.Count;

				var free = new List<AssemblyFile>();
				foreach (var file in toAdd)
					if (!required.ContainsValue(file))
						free.Add(file);

				if (free.Count == 0)
					throw new Exception("Circular initialization dependency detected:\n"
						+ util.Join(toAdd.Select(p => p.Key), "\n"));

				foreach (var file in free)
				{
					output.Add(file);
					toAdd.Remove(file);
					declares.KillValues(file);
				}

				foreach (var key in required.Keys().ToList())
				{
					if (!declares.Contains(key))
						required.Kill(key);
				}
			}
			return output;
		}
	}

	public class WingraSymbols
	{
		public Dictionary<string, AssemblyFile> AssemblyMap = new Dictionary<string, AssemblyFile>();
		public Dictionary<string, WingraBuffer> FileMap = new Dictionary<string, WingraBuffer>();

		public string WhereIsStack(Scope lvl)
		{
			if (lvl.Source.FileCode == null)
				return "[non-tracked query]";
			var key = lvl.Source.FileCode.Key;
			var shortName = lvl.Source.FileCode.Name;
			var line = lvl.CurrentLinePointer;

			if (AssemblyMap.ContainsKey(key))
			{
				var assm = AssemblyMap[key];
				string funcKey = lvl.Source.FileCode.FindFunctionName(lvl.Source);
				return shortName + " :: " + funcKey + " +" + line;
			}
			else return shortName + " :: ???";
		}

		public string GetCodeAt(Scope lvl)
		{
			if (lvl.Source.FileCode == null)
				return "???";
			var key = lvl.Source.FileCode.Key;

			if (FileMap.ContainsKey(key))
			{
				var file = FileMap[key];
				var assFunc = GetAssemblyCode(lvl.Source);
				var fileLine = assFunc.GetFileLineFromCodeLine(lvl.CurrentLinePointer);
				if (fileLine >= 0 && fileLine < file.Lines)
					return "[ln " + fileLine + "]  " + file.TextAtLine(fileLine).Trim().Replace("\t", "  ");
			}
			return "[unknown code]";
		}

		public AssemblyCode GetAssemblyCode(CodeBlock code)
		{
			if (code.FileCode == null) return null;
			var funcName = code.FileCode.FindFunctionName(code);
			var filekey = code.FileCode.Key;
			if (!AssemblyMap.ContainsKey(filekey)) return null;
			var asmFile = AssemblyMap[filekey];
			return asmFile.GetByName(funcName);
		}

		public WingraBuffer GetBuffer(CodeBlock code)
		{
			if (code.FileCode == null) return null;
			var filekey = code.FileCode.Key;
			if (!FileMap.ContainsKey(filekey)) return null;
			return FileMap[filekey];
		}

		public void Register(WingraBuffer buffer, AssemblyFile ass)
		{
			FileMap[buffer.Key] = buffer;
			AssemblyMap[buffer.Key] = ass;
		}
		public void Register(string name, AssemblyFile ass)
		{
			AssemblyMap[name] = ass;
		}
	}
}
