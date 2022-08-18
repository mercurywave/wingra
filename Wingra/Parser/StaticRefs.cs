using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	class SStaticPath : SExpressionComponent, ICanEmitInline//, IEmitSymbol
	{
		RelativeTokenReference[] _path;
		List<string> _usingPaths;
		string _fileKey;
		int _fileLine;
		public SStaticPath(RelativeTokenReference[] tokes)
		{
			if (tokes.Length < 1) throw new Exception("error parsing tokens for static path");
			_path = tokes;
			if (_path.Any(t => t.Token.Type == eToken.Dot))
				throw new ParserException("didn't expect a . in path at this time - likely compiler bug", _path[0]);
		}
		public SStaticPath(RelativeTokenReference[] tokes, List<string> usingPaths) : this(tokes)
		{
			_usingPaths = usingPaths;
		}
		public SStaticPath(RelativeTokenReference[] tokes, string declarePath) : this(tokes, new List<string>() { declarePath }) { }

		internal override void OnAddedToTree(ParseContext context)
		{
			// saving these off now so that we can resolve using correct scoping later
			// this matters if the function gets inlined, so we can find things from the source file
			_fileKey = context.FileKey;
			_fileLine = context.FileLine;
			base.OnAddedToTree(context);
		}

		void Resolve(Compiler compiler, out string type, out string path, out string fullPath, out string[] dynamicPath)
		{
			fullPath = compiler.StaticMap.ResolvePath(_fileKey, _fileLine, _path, _usingPaths, out type, out path, out _, out dynamicPath);
			if (type != StaticMapping.FILE_ABS && type != StaticMapping.DATA_ABS)
				throw new CompilerException("absolute path is badly formatted", -1);
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			Resolve(compiler, out var type, out var path, out var fullPath, out var dynamicPath);
			if (!TryEmitInline(compiler, file, func, asmStackLevel, errors, parent, type, fullPath, dynamicPath))
			{
				if (type == StaticMapping.DATA_ABS)
					func.Add(asmStackLevel, eAsmCommand.LoadPathData, path);
				if (type == StaticMapping.FILE_ABS)
					func.Add(asmStackLevel, eAsmCommand.LoadPathFile, util.Piece(fullPath, "|", 2, 3));
				foreach (var dyn in dynamicPath)
					func.Add(asmStackLevel, eAsmCommand.DotAccess, dyn);
			}
		}

		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			Resolve(compiler, out var type, out var path, out var fullPath, out var dynamicPath);
			return TryEmitInline(compiler, file, func, asmStackLevel, errors, parent, type, fullPath, dynamicPath);
		}
		bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent, string type, string path, string[] dynamicPath)
		{
			if (!compiler.Optimizations)
			{
				//TODO: ensure static compiler optimizations always apply
				return false;
			}
			if (dynamicPath.Length > 0) return false;
			var exp = compiler.StaticMap.TryGetConstant(path);
			if (exp == null) return false;

			var emitter = exp as ICanEmitInline;
			if (emitter == null) return false;
			emitter.TryEmitInline(compiler, file, func, asmStackLevel, errors, this);
			return true;
		}

		public string ToText() => StaticMapping.JoinPath(_path);

		// I don't think I want to support this currently. e.g. "var = obj.$foo.bar" - ambiguous and weird with other uses
		//public void EmitPropertyAction(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		//{
		//	var path = ResolveNamespace(compiler.StaticMap, file.Key, errors);
		//	func.Add(asmStackLevel, eAsmCommand.DotAccess, 0, path);
		//}

		public static RelativeTokenReference[] CleanPath(RelativeTokenReference[] path)
		{
			var okay = new List<RelativeTokenReference>();
			for (int i = 0; i < path.Length; i += 2)
				okay.Add(path[i]);
			for (int i = 1; i < path.Length; i += 2)
				if (path[i].Token.Type != eToken.Dot)
					throw new ParserException("expected . in path", path[i]);
			return okay.ToArray();
		}

	}

	class SStaticDeclaredPath : SExpressionComponent
	{
		RelativeTokenReference[] _path;
		string _declaringPath;
		eStaticType _type;
		public SStaticDeclaredPath(eStaticType type, RelativeTokenReference[] tokes)
		{
			if (tokes.Length < 1) throw new Exception("error parsing tokens for static path");
			_type = type;
			_path = tokes;
			if (_path.Any(t => t.Token.Type == eToken.Dot))
				throw new ParserException("didn't expect a . in path at this time - likely compiler bug", _path[0]);
		}
		public SStaticDeclaredPath(eStaticType type, RelativeTokenReference[] tokes, string declarePath) : this(type, tokes)
		{
			_declaringPath = declarePath;
		}

		public void Reserve(Compiler compiler, string fileKey, int fileLine)
		{
			compiler.StaticMap.ReserveNamespace(fileKey, fileLine, _path, _declaringPath, _type);
		}
		public void Reserve(Compiler compiler, string fileKey, int fileLine, _SfunctionDef funcDef, bool isMethod)
		{
			if (_type == eStaticType.External)
				Reserve(compiler, fileKey, fileLine);
			else
				compiler.StaticMap.ReserveNamespace(fileKey, fileLine, _path, _declaringPath, eStaticType.Function, funcDef);
		}
		public void Reserve(Compiler compiler, string fileKey, int fileLine, SExpressionComponent exp)
		{
			if (_type == eStaticType.External)
				Reserve(compiler, fileKey, fileLine);
			else
				compiler.StaticMap.ReserveNamespace(fileKey, fileLine, _path, _declaringPath, eStaticType.Constant, null, exp);
		}

		void Resolve(Compiler compiler, FileAssembler file, int fileLine, out string type, out string path)
		{
			compiler.StaticMap.ResolveNamespace(file.Key, fileLine, _path, _declaringPath, out type, out path);
			if (type != StaticMapping.DATA && type != StaticMapping.FILE)
				throw new CompilerException("absolute path is badly formatted", -1);
		}

		// why isn't this overriding emit as assignment instead?
		//  -- I think because you do not want to be able to accidentally save to data in a function
		internal void EmitSave(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			Resolve(compiler, file, func.CurrentFileLine, out var type, out var path);
			if (type == StaticMapping.DATA)
				func.Add(asmStackLevel, eAsmCommand.StoreToPathData, path);
			else if (type == StaticMapping.FILE)
				func.Add(asmStackLevel, eAsmCommand.StoreToFileConst, path);
			else throw new CompilerException("could not resolve path " + path, func.CurrentFileLine);
		}
		internal void EmitSaveEnum(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel)
		{
			Resolve(compiler, file, func.CurrentFileLine, out var type, out var path);
			if (type == StaticMapping.DATA)
				func.Add(asmStackLevel, eAsmCommand.StoreEnumToPathData, path);
			else if (type == StaticMapping.FILE)
				func.Add(asmStackLevel, eAsmCommand.StoreEnumToFileConst, path);
			else throw new CompilerException("could not resolve enum path " + path, func.CurrentFileLine);
		}
		public string ToText() => StaticMapping.JoinPath(_path);
		public string ResolvedToText() => util.AppendPiece(_declaringPath, ".", StaticMapping.JoinPath(_path));

	}

	class SStaticFunctionCall : SExpressionComponent, ICanBeProperty, ICanAwait, IDecompose
	{
		RelativeTokenReference[] _path;
		List<string> _usingPaths;
		internal List<SExpressionComponent> _params; // may be null!
		string _fileKey;
		int _fileLine;
		internal bool _isAsync = false;
		int _numToDecompose = 1;
		public SStaticFunctionCall(RelativeTokenReference[] path, List<string> usingPaths, List<SExpressionComponent> paramList)
		{
			if (path.Length < 1) throw new Exception("error parsing tokens for static path");
			_path = path;
			_usingPaths = usingPaths;
			_params = paramList;
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			// saving these off now so that we can resolve using correct scoping later
			// this matters if the function gets inlined, so we can find things from the source file
			_fileKey = context.FileKey;
			_fileLine = context.FileLine;
			base.OnAddedToTree(context);
		}
		public void FlagAsAwaiting()
			=> _isAsync = true;

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
			=> EmitInternal(compiler, file, func, asmStackLevel, errors, parent, false, _isAsync);
		public void EmitPropertyAction(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
			=> EmitInternal(compiler, file, func, asmStackLevel, errors, parent, true, _isAsync);

		void EmitInternal(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent, bool asMethod, bool asAsync)
		{
			var path = compiler.StaticMap.ResolvePath(_fileKey, _fileLine, _path, _usingPaths, out var type, out var shortPath, out var targetFKey, out var dynamicPath);

			var fn = compiler.StaticMap.TryGetFunction(path);
			if (fn != null && compiler.SanityChecks)
			{
				if (_isAsync && !fn._isAsync) throw new CompilerException("don't need to await non-async function", func.CurrentFileLine);
				if (!_isAsync && fn._isAsync) throw new CompilerException("function is async, use await or arun", func.CurrentFileLine);
				if (_params != null)
				{
					for (int i = 0; i < _params.Count; i++)
					{
						if (i >= fn.Parameters.Count)
						{
							if (fn.HasParamArray) break;
							throw new CompilerException("passing too many parameters to function", func.CurrentFileLine);
						}
						if (fn.Parameters[i].OwnedOnly)
						{
							if (_params[i] is SIdentifier)
								throw new CompilerException("function expects ownership to be passed", func.CurrentFileLine);
						}
					}
				}
				for (int i = (_params == null) ? 0 : _params.Count; i < fn.Parameters.Count; i++)
				{
					if (!fn.Parameters[i].IsOptional)
						throw new CompilerException("not all required parameters are passed", func.CurrentFileLine);
				}

				if (asMethod && !fn._isMethod)
					throw new CompilerException("function is not a method", func.CurrentFileLine);

				if (!asMethod && fn._isMethod)
					throw new CompilerException("function is a method", func.CurrentFileLine);

				if (fn._isThrow && !func.IsInErrorTrap())
					throw new CompilerException("function which throws is not trapped by caller", func.CurrentFileLine);

				if (_numToDecompose > 1 && _numToDecompose > fn._returnParams.Count)
					throw new CompilerException("function call asks for " + _numToDecompose + " return values, but function only returns " + fn._returnParams.Count, func.CurrentFileLine);
			}

			if (dynamicPath.Length > 0 || !TryEmitInline(compiler, file, func, asmStackLevel, errors, parent, asMethod, fn))
			{
				var pct = _params != null ? _params.Count : 0;
				if (_params != null)
					foreach (var p in _params)
						p.EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);

				if (_isAsync) func.Add(asmStackLevel, eAsmCommand.BeginAwaitCall);
				if (pct > 0) func.Add(asmStackLevel, eAsmCommand.PassParams, pct);
				if (dynamicPath.Length > 0)
					// implementing this is possible, but probably requires a chunk of code changes
					// I don't see a strong use case currently
					throw new NotImplementedException();
				func.Add(asmStackLevel, GetCommand(type, asMethod), path);

				func.Add(asmStackLevel, eAsmCommand.ReadReturn, _numToDecompose);
			}
		}
		bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent, bool asMethod, _SfunctionDef fn)
		{
			if (!compiler.Optimizations) return false;
			if (fn == null) return false;
			if (_numToDecompose > 1) return false;

			var pcount = (_params == null) ? 0 : _params.Count;
			if (!fn.CanBeInlined(pcount)) return false;

			var mapper = new Dictionary<string, Func<FunctionFactory, eAsmCommand, int, bool>>();
			var inline = fn.GenerateInline(compiler, file, 0, errors);
			if (inline == null) return false;
			var usage = inline.GetSymbolUseCount();
			int GetUses(string key) => usage.ContainsKey(key) ? usage[key] : 0;

			if (fn._isMethod)
			{
				if (GetUses(LambdaPointer.THIS) > 0)
				{
					// special case where the function can use the this variable on the stack without needing to save it off
					//::.Foo() \ this.Var
					if (GetUses(LambdaPointer.THIS) == 1 && inline.HasCommandAt(eAsmCommand.Load, LambdaPointer.THIS, 0))
						mapper.Add(LambdaPointer.THIS, (ff, cmd, sl) => { return true; }); // do not emit this load
					else
					{
						// if this is used in any other way, we do need to save off and remap a copy
						var temp = func.GetReserveUniqueTemp("inThis");
						func.Add(asmStackLevel, eAsmCommand.StoreLocal, temp);
						mapper.Add(LambdaPointer.THIS, (ff, cmd, sl) =>
						{
							ff.Add(sl, cmd, temp);
							return true;
						});
					}
				}
				else
					// there is a this on the stack because this is a method,
					// but the target function doesn't technically use it
					// if we don't clean this up, the this leaks
					func.Add(asmStackLevel, eAsmCommand.ClearRegisters, 1);
			}

			for (int i = 0; i < pcount; i++)
			{
				var source = _params[i];
				var target = fn.Parameters[i];
				if (source is SIdentifier)
				{
					// if the input is a local, the function can easily reference that local
					var ident = source as SIdentifier;
					mapper.Add(target.Identifier, (ff, cmd, sl) =>
					{
						ff.Add(sl, cmd, ident.Symbol);
						return true;
					});
				}
				else if (GetUses(target.Identifier) == 1 || source is SLiteralString || source is SLiteralNumber || source is SLiteralNull || source is SLiteralBool)
				{
					// if we're only using this input once or it's simple, we can just generate it on the fly when needed
					mapper.Add(target.Identifier, (ff, cmd, sl) =>
					{
						// must be using this input as a scratchpad, can't really deal with that
						if (FunctionFactory.CmdSavesVar(cmd))
							return false;
						source.EmitAssembly(compiler, file, ff, sl, errors, this);
						return true;
					});
				}
				else
				{
					// if the input is used multiple times 
					var temp = func.GetReserveUniqueTemp("parTmp");
					source.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
					func.Add(asmStackLevel, eAsmCommand.StoreLocal, temp);
					mapper.Add(target.Identifier, (ff, cmd, sl) =>
					{
						ff.Add(sl, cmd, temp);
						return true;
					});
				}
			}

			// rename any temp variables, so as not to conflict in weird ways
			// mostly just matters for nested inlining
			foreach (var vari in inline._declaredVars)
			{
				if (mapper.ContainsKey(vari)) continue;
				var temp = func.GetReserveUniqueTemp(vari);
				mapper.Add(vari, (ff, cmd, sl) =>
				{
					ff.Add(sl, cmd, temp);
					return true;
				});
			}

			return func.AddInlineFunc(inline, mapper, asmStackLevel);
		}
		protected virtual eAsmCommand GetCommand(string type, bool asMethod)
			=> GetCommandFromPath(type, asMethod);
		internal static eAsmCommand GetCommandFromPath(string type, bool asMethod)
		{
			if (type == StaticMapping.DATA_ABS)
				return asMethod ? eAsmCommand.CallPathMethod : eAsmCommand.CallPathFunc;
			if (type == StaticMapping.FILE_ABS)
				return asMethod ? eAsmCommand.CallFileMethod : eAsmCommand.CallFileFunc;
			else throw new CompilerException("absolute path is badly formatted", -1);
		}

		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			if (_params != null)
				foreach (var p in _params)
					yield return p;
		}

		public void RequestDecompose(int numRequested)
			=> _numToDecompose = numRequested;
	}

	class SUsing : SStatement
	{
		List<SStaticPath> _paths;
		public SUsing(int fileLine, List<SStaticPath> paths) : base(fileLine)
		{
			_paths = paths;
		}
		public SUsing(int fileLine, SStaticPath path)
			: this(fileLine, new List<SStaticPath>() { path }) { }

		internal override void OnAddedToTree(ParseContext context)
		{
			foreach (var p in _paths)
				context.Scope.RegisterUsingNamespaceRef(p.ToText(), false);
			base.OnAddedToTree(context);
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent) { }
	}
}
