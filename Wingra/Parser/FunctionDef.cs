using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	class _SfunctionDef : SScopeStatement
	{
		protected RelativeTokenReference _identifier;
		List<SParameter> _parameters;
		internal List<SIdentifier> _returnParams;
		public string Identifier => _identifier.Token.Token;
		internal bool _doesYield;
		internal bool _isMethod;
		internal bool _isAsync;
		internal bool _isThrow;
		bool _oneLiner = false;
		public _SfunctionDef(int fileline, RelativeTokenReference identifier, List<SParameter> parameters, List<SIdentifier> returnParams, bool doesYield, bool isMethod, bool isAsync, bool isThrow, SExpressionComponent oneLiner) : base(fileline)
		{
			_identifier = identifier;
			_parameters = parameters;
			_returnParams = returnParams;
			_doesYield = doesYield;
			_isMethod = isMethod;
			_isAsync = isAsync;
			_isThrow = isThrow;
			if (oneLiner != null)
			{
				if (returnParams.Count > 1)
					throw new ParserException("=> syntax cannot return multiple parameter");
				SyntaxNode node;
				if (returnParams.Count == 0)
					node = new SExpression(fileline, oneLiner);
				else
					node = new SReturn(fileline, new List<SExpressionComponent>() { oneLiner });
				AddChild(node);
				_oneLiner = true;
			}
		}
		internal override void OnAddedToTree(ParseContext context)
		{
			if (_oneLiner)
				foreach (var child in Children)
					child.OnAddedToTree(context);
			base.OnAddedToTree(context);
		}
		public override void AddChild(SyntaxNode node)
		{
			if (_oneLiner)
				throw new ParserException("=> functions cannot have child scope");
			if(node is SArrowReturnStatement)
			{
				if (Children.Any())
					throw new ParserException("=> returns must be the first line of the function");
				if(_returnParams.Count != 1)
					throw new ParserException("=> returns require a function with a single return value");
				_oneLiner = true;
			}
			base.AddChild(node);
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var lamb = EmitBody(compiler, file, func, asmStackLevel, errors);
			EmitRegister(compiler, file, func, lamb, asmStackLevel, errors, parent);
		}
		internal FunctionFactory EmitBody(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors)
		{
			if (compiler.SanityChecks)
			{
				bool foundOpt = false;
				foreach (var p in _parameters)
				{
					if (p.IsOptional && !foundOpt) foundOpt = true;
					else if (!p.IsOptional && foundOpt)
						throw new CompilerException(p.Identifier + " is not optional, but prior parameters are", func.CurrentFileLine);
				}
			}
			var lamb = file.GenFunction(Identifier);
			foreach (var p in _parameters)
				p.EmitAssembly(compiler, file, lamb, asmStackLevel, errors, this);
			if (compiler.SanityChecks)
				foreach (var p in _parameters)
					p.EmitChecks(compiler, file, lamb, asmStackLevel, errors, this);
			lamb.AddReturnParam(_returnParams.Select(r => r.Symbol).ToArray());
			if (_isAsync) lamb.IsAsync = true;
			if (_doesYield) lamb.SetupIterator(_returnParams.Count);
			if (_isMethod) lamb.ReserveVariable("this");
			if (_isThrow) lamb.CanThrow = true;
			EmitChildren(compiler, file, lamb, asmStackLevel, errors);
			if (compiler._isIDE && !compiler._isAsmDebug && lamb.GetStackDelta() != 0)
				throw new CompilerException("function is leaking registers (compiler bug)", func.CurrentFileLine);
			return lamb;
		}

		internal bool CanBeInlined(int passingParams)
		{
			if (passingParams > _parameters.Count) return false;
			if (Children.Count() != 1) return false;
			var inner = Children.First();
			if (_returnParams.Count == 0)
			{
				if (inner is SExpression) return true;
				return false;
			}
			if (_returnParams.Count != 1 || _doesYield) return false;
			if (_parameters.Any(p => p is SMultiParameter)) return false;
			if (inner is SReturn) return true;
			if (inner is SAssign)
			{
				var ass = inner as SAssign;
				if (ass.NumToDecompose != 1) return false;
				if (ass._left.Count != 1) return false;
				if (ass._left[0] is SIdentifier)
				{
					var ident = ass._left[0] as SIdentifier;
					if (ident.Symbol == _returnParams[0].Symbol) return true;
				}
				return false;
			}
			return false;
		}

		public FunctionFactory GenerateInline(Compiler compiler, FileAssembler file, int asmStackLevel, ErrorLogger errors)
		{
			FunctionFactory temp = new FunctionFactory();
			temp.AllowUndefined = true;

			// TODO: scan tree for side effects and cancel out
			// I can't think of any side effects now, but I'm suspicious
			var state = Children.First() as SStatement;
			if (state == null) return null;
			if (state is IHaveChildScope) return null;
			if (_isThrow) return null;
			foreach (var child in state.IterExpressionsRecursive())
				if (child is SReserveIdentifierExp || child is SLambda || child is SLambdaMethod || child is SExecute || child is SFree || child is SOneLiner || child is SAvowExpression)
					return null;

			var inner = Children.First() as ICanEmitInline; // assumes you already checked CanBeInlined
			if (inner == null) throw new NotImplementedException();
			if (compiler.InlineDepth > 5) return null;
			using (new ODisposable(() => compiler.InlineDepth++, () => compiler.InlineDepth--))
				try
				{
					var logger = new MinimalErrorLogger();

					// technically this inlining could mean that the function would run with an owned
					// reference when it wouldn't if the function wasn't inlined
					// I don't think I really care, since that's just a sanity check
					foreach (var p in _parameters)
						p.EmitChecks(compiler, file, temp, asmStackLevel, logger, this);

					// It is a bit weird that we're passing file in here
					// there's a chance this has side effects because of that
					inner.TryEmitInline(compiler, file, temp, asmStackLevel, logger, this);
				}
				catch (Exception ) { return null; }
			// MAYBE: if you call a global function and that function just uses a local function,
			// the global function can't be inlined because the caller can't access the local in another file
			// might not be worth the overhead? maybe we could detect this sooner?
			return temp;
		}



		protected virtual void EmitRegister(Compiler compiler, FileAssembler file, FunctionFactory func, FunctionFactory lamb, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
			=> throw new NotImplementedException();
		public List<SParameter> Parameters => _parameters;
		public bool HasParamArray => _parameters.Count > 0 && _parameters[_parameters.Count - 1] is SMultiParameter;
	}

	internal class SGlobalFunctionDef : _SfunctionDef, IExportGlobalSymbol
	{
		public SGlobalFunctionDef(int fileline
			, RelativeTokenReference identifier
			, List<SParameter> parameters
			, List<SIdentifier> returnParams
			, bool doesYield
			, bool isMethod
			, bool isAsync
			, bool isThrow
			, SExpressionComponent oneLiner)
				: base(fileline, identifier, parameters, returnParams, doesYield, isMethod, isAsync, isThrow, oneLiner)
		{ }

		internal override void OnAddedToTree(ParseContext context)
		{
			context.Comp.StaticMap.AddStaticGlobal(Identifier, eStaticType.Function, context.FileKey, FileLine, this);
			base.OnAddedToTree(context);
		}

		public IEnumerable<string> GetExportableSymbolsInside(SyntaxNode parent)
		{
			yield return Identifier;
		}

		protected override void EmitRegister(Compiler compiler, FileAssembler file, FunctionFactory func, FunctionFactory lamb, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			file.FuncDefRoutine.Add(asmStackLevel, eAsmCommand.DeclareStaticFunction, 0, lamb.UniqNameInFile);
			file.FuncDefRoutine.Add(asmStackLevel, eAsmCommand.StoreToPathData, Identifier);
		}
	}

	internal class SFileFunctionDef : _SfunctionDef
	{
		public SFileFunctionDef(Compiler compiler, string fileKey, int fileline
			, RelativeTokenReference identifier
			, List<SParameter> parameters
			, List<SIdentifier> returnParams
			, bool doesYield
			, bool isMethod
			, bool isAsync
			, bool isThrow
			, SExpressionComponent oneLiner)
				: base(fileline, identifier, parameters, returnParams, doesYield, isMethod, isAsync, isThrow, oneLiner)
		{
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			context.Comp.StaticMap.AddFilePath(context.FileKey, Identifier, eStaticType.Function, FileLine, this);
			base.OnAddedToTree(context);
		}

		protected override void EmitRegister(Compiler compiler, FileAssembler file, FunctionFactory func, FunctionFactory lamb, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			file.FuncDefRoutine.Add(asmStackLevel, eAsmCommand.PushString, 0, Identifier);
			file.FuncDefRoutine.Add(asmStackLevel, eAsmCommand.DeclareFunction, 0, lamb.UniqNameInFile);
		}
	}

	internal class SLibFunctionDef : _SfunctionDef
	{
		SStaticDeclaredPath _declaredPath;
		public SLibFunctionDef(Compiler compiler, int fileline
			, SStaticDeclaredPath declaredPath
			, RelativeTokenReference identifier
			, List<SParameter> parameters
			, List<SIdentifier> returnParams
			, bool doesYield
			, bool isMethod
			, bool isAsync
			, bool isThrow
			, SExpressionComponent oneLiner)
				: base(fileline, identifier, parameters, returnParams, doesYield, isMethod, isAsync, isThrow, oneLiner)
		{
			_declaredPath = declaredPath;
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			_declaredPath.Reserve(context.Comp, context.FileKey, context.FileLine, this, _isMethod);
			base.OnAddedToTree(context);
		}

		protected override void EmitRegister(Compiler compiler, FileAssembler file, FunctionFactory func, FunctionFactory lamb, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var header = file.FuncDefRoutine;
			header.Add(asmStackLevel, eAsmCommand.DeclareStaticFunction, 0, lamb.UniqNameInFile);
			_declaredPath.EmitSave(compiler, file, header, 0, errors, this);
		}
	}
	internal class SLocalFuncDef : _SfunctionDef
	{
		public SLocalFuncDef(int fileline
			, RelativeTokenReference identifier
			, List<SParameter> parameters
			, List<SIdentifier> returnParams
			, bool doesYield
			, bool isMethod
			, bool isAsync
			, bool isThrow
			, SExpressionComponent oneLiner)
				: base(fileline, identifier, parameters, returnParams, doesYield, isMethod, isAsync, isThrow, oneLiner)
		{ }

		protected override void EmitRegister(Compiler compiler, FileAssembler file, FunctionFactory func, FunctionFactory lamb, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			// I haven't really thought about this very hard, this is just ported from Kilt...
			func.Add(asmStackLevel, eAsmCommand.CreateLambda, 0, lamb.UniqNameInFile);
			func.Add(asmStackLevel, eAsmCommand.StoreNewLocal, 0, Identifier);
		}
	}

	internal class SDimMethod : _SfunctionDef
	{
		public SDimMethod(int fileline
			, RelativeTokenReference identifier
			, List<SParameter> parameters
			, List<SIdentifier> returnParams
			, bool doesYield
			, bool isAsync
			, bool isThrow
			, SExpressionComponent oneLiner)
				: base(fileline, identifier, parameters, returnParams, doesYield, true, isAsync, isThrow, oneLiner)
		{ }

		protected override void EmitRegister(Compiler compiler, FileAssembler file, FunctionFactory func, FunctionFactory lamb, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			// I haven't really thought about this very hard, this is just ported from Kilt...
			func.Add(asmStackLevel, eAsmCommand.CreateLambda, 0, lamb.UniqNameInFile);
			func.Add(asmStackLevel, eAsmCommand.DimSetString, Identifier);
		}
	}

	public class SParameter : SyntaxNode
	{
		string _identifier;
		bool _optional, _ownedOnly;
		public SParameter(string identifier, bool optional, bool ownedOnly) : base()
		{
			_identifier = identifier;
			_optional = optional;
			_ownedOnly = ownedOnly;
		}
		public string Identifier => _identifier;
		public bool IsOptional => _optional;
		public bool OwnedOnly => _ownedOnly;
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.ReadParam, Identifier);
		}
		internal void EmitChecks(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_ownedOnly)
				func.Add(asmStackLevel, eAsmCommand.AssertOwnedVar, Identifier);
		}
		public virtual string GetDisplayString() => (_optional ? "?" : "") + (_ownedOnly ? "&" : "") + _identifier;
	}

	public class SMultiParameter : SParameter
	{
		public SMultiParameter(string identifier, bool optional, bool ownedOnly) : base(identifier, optional, ownedOnly) { }
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.ReadMultiParam, Identifier);
		}
		public override string GetDisplayString() => base.GetDisplayString() + "[]";
	}

	interface ICanEmitInline
	{
		bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent);
	}
}
