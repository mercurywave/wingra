using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Parser
{
	#region globals

	class SAssignScratch : SStatement, IHaveIdentifierSymbol, IExportGlobalSymbol
	{
		RelativeTokenReference _left;
		SExpressionComponent _right;
		eToken _operator;
		public string Symbol => _left.Token.Token;
		bool _isGlobal;

		// not actually used - registry is just assumed to be a dimmed structure and needs no initialization
		// I can't think of a use case for this, but I wrote it and it's fairly harmless
		bool _isRegistry;

		public SAssignScratch(int fileLine, RelativeTokenReference left, eToken op, SExpressionComponent right, bool isGlobal, bool isRegistry) : base(fileLine)
		{
			_left = left;
			_operator = op;
			_right = right;
			_isGlobal = isGlobal;
			_isRegistry = isRegistry;
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			context.StaticMap.TryRegisterScratch(Symbol, context.FileKey, _isGlobal);
			base.OnAddedToTree(context);
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var header = file.InitRoutine;
			if (_isRegistry) header = file.RegistryRoutine;

			_right.EmitAssembly(compiler, file, header, asmStackLevel, errors, this);

			header.Add(asmStackLevel, eAsmCommand.SetFileContext, file.Key);
			header.Add(asmStackLevel, eAsmCommand.StoreNewScratch, Symbol);
		}
		public IEnumerable<string> GetExportableSymbolsInside(SyntaxNode parent)
		{
			return new List<string>() {
				Symbol
			};
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _right;
		}
	}

	// scratch var
	class SReserveScratch : SExpressionComponent, IHaveIdentifierSymbol, IExportGlobalSymbol
	{
		internal RelativeTokenReference _source;
		bool _isGlobal;
		bool _isRegistry;
		public SReserveScratch(RelativeTokenReference toke, bool isGlobal, bool isRegistry)
		{
			_source = toke;
			_isGlobal = isGlobal;
			_isRegistry = isRegistry;
		}
		internal override void OnAddedToTree(ParseContext context)
		{
			context.StaticMap.TryRegisterScratch(_source.Token.Token, context.FileKey, _isGlobal);
			base.OnAddedToTree(context);
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_isRegistry)
			{
				var header = file.RegistryRoutine;
				header.Add(0, eAsmCommand.DimArray, 4); // registries are just assumed to have a dim
				header.Add(0, eAsmCommand.SetFileContext, file.Key);
				header.Add(0, eAsmCommand.StoreNewScratch, Symbol);
			}
			else
			{
				var header = file.InitRoutine;
				header.Add(0, eAsmCommand.SetFileContext, file.Key);
				header.Add(0, eAsmCommand.ReserveScratch, Symbol);
			}
			// TODO: should this return a value? it is ostensibly an expression
			// why is it ever an expression?
		}

		public IEnumerable<string> GetExportableSymbolsInside(SyntaxNode parent)
		{
			yield return Symbol;
		}

		public string Symbol => _source.Token.Token;
	}
	#endregion

	class SAssign : SStatement, IDeclareVariablesAtScope, IWillDecompose, ICanEmitInline
	{
		internal List<SExpressionComponent> _left;
		protected SExpressionComponent _right;
		eToken _operator;

		public int NumToDecompose => _left.Count;

		public SAssign(int fileLine, List<SExpressionComponent> left, eToken op, SExpressionComponent right) : base(fileLine)
		{
			_left = left;
			_operator = op;
			_right = right;
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_right.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			//if (_left.Count > 1)
			//	func.Add(asmStackLevel, eAsmCommand.Decompose, _left.Count);
			foreach (var target in _left)
			{
				eAsmCommand? cmd = null;
				string symbol = "";

				symbol = _GetTargetSymbol(target);

				if (target is SReserveIdentifierExp)
					cmd = eAsmCommand.StoreNewLocal;
				else if (target is SIdentifier)
					cmd = eAsmCommand.StoreLocal;

				if (cmd.HasValue)
					func.Add(asmStackLevel, cmd.Value, symbol);
				//else if (target is SKeyAccess)
				//	(target as SKeyAccess).EmitAssign(file, func, asmStackLevel, errors, this);
				else
				{
					target.EmitAsAssignment(compiler, file, func, asmStackLevel, errors, this);
				}
			}
		}

		// helper, potentially very specific, returns null if it isn't that simple (like an expression)
		string _GetTargetSymbol(SExpressionComponent target)
		{
			if (target is SReserveScratch)
				return (target as SReserveScratch).Symbol;
			else if (target is SReserveIdentifierExp)
				return (target as SReserveIdentifierExp).Symbol;
			else if (target is SIdentifier)
				return (target as SIdentifier).Symbol;
			return null;
		}

		public IEnumerable<string> GetDeclaredSymbolsInside(SyntaxNode parent)
		{
			List<string> list = new List<string>();
			foreach (var target in _left)
				if (target is SReserveIdentifierExp)
				{
					var name = _GetTargetSymbol(target);
					if (name != null)
						list.Add(name);
				}
			return list;
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			foreach (var left in _left)
				yield return left;
			yield return _right;
		}

		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_right.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			return true;
		}
	}

	class SOpAssign : SStatement, ICanEmitInline
	{
		internal SExpressionComponent _left;
		protected SExpressionComponent _right;
		eToken _operator; // operator is the math symbol here

		public SOpAssign(int fileLine, SExpressionComponent left, eToken op, SExpressionComponent right) : base(fileLine)
		{
			_left = left;
			_operator = op;
			_right = right;
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (BaseToken.DoesShortCircuit(_operator))
			{
				var local = _left as SIdentifier;
				if (local != null && _operator == eToken.QuestionMark)
				{
					// this is a potentially hot code path, but _maybe_ we don't need this special case command
					func.Add(asmStackLevel, eAsmCommand.TestIfUninitialized, local.Symbol);
					func.Add(asmStackLevel + 1, eAsmCommand.DoIfTest);
					EmitAssignment(compiler, file, func, asmStackLevel + 2, errors, parent);
				}
				else
				{
					_left.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
					func.Add(asmStackLevel, getShortcutCmd(_operator));
					func.Add(asmStackLevel + 1, eAsmCommand.Pop);
					_right.EmitAssembly(compiler, file, func, asmStackLevel + 1, errors, this);
					_left.EmitAsAssignment(compiler, file, func, asmStackLevel, errors, this);
				}
			}
			else
			{
				_left.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				_right.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				func.Add(asmStackLevel, getCmd(_operator));
				_left.EmitAsAssignment(compiler, file, func, asmStackLevel, errors, this);
			}
		}

		eAsmCommand getShortcutCmd(eToken tok)
		{
			switch (tok)
			{
				case eToken.QuestionMark: return eAsmCommand.ShortCircuitNotNull;
				case eToken.And: return eAsmCommand.ShortCircuitFalse;
				case eToken.Or: return eAsmCommand.ShortCircuitTrue;
				default: throw new NotImplementedException();
			}
		}

		eAsmCommand getCmd(eToken tok)
		{
			switch (tok)
			{
				case eToken.Add: return eAsmCommand.Add;
				case eToken.Subtract: return eAsmCommand.Subtract;
				case eToken.Multiply: return eAsmCommand.Multiply;
				case eToken.Divide: return eAsmCommand.Divide;
				default: throw new NotImplementedException();
			}
		}

		private void EmitAssignment(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_right.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			_left.EmitAsAssignment(compiler, file, func, asmStackLevel, errors, this);
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _left;
			yield return _right;
		}

		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_right.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			return true;
		}
	}

	class SReserveIdentifier : SStatement, IHaveIdentifierSymbol, IDeclareVariablesAtScope
	{
		internal RelativeTokenReference _source;
		public SReserveIdentifier(RelativeTokenReference toke, int fileLine) : base(fileLine)
		{
			_source = toke;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.DeclareVariable(Symbol, asmStackLevel);
		}

		public IEnumerable<string> GetDeclaredSymbolsInside(SyntaxNode parent)
		{
			yield return Symbol;
		}

		public string Symbol => _source.Token.Token;
	}

	// (a,b) : value
	class SMultiAssignExp : SExpressionComponent, IDeclareVariablesAtScope
	{
		List<SExpressionComponent> _comp;
		public SMultiAssignExp(List<SExpressionComponent> components)
		{
			if (components.Count == 0) throw new Exception("multi assignment expression needs at least 1 input");
			_comp = components;
		}

		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			return _comp;
		}
		internal override void OnAddedToTree(ParseContext context)
		{
			foreach (var c in _comp)
				c.OnAddedToTree(context);
			base.OnAddedToTree(context);
		}

		public IEnumerable<string> GetDeclaredSymbolsInside(SyntaxNode parent)
		{
			foreach (var c in _comp)
				if (c is IDeclareVariablesAtScope)
					foreach (var name in (c as IDeclareVariablesAtScope).GetDeclaredSymbolsInside(this))
						yield return name;
		}
		internal override void EmitAsAssignment(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_comp[0].EmitAsAssignment(compiler, file, func, asmStackLevel, errors, this);
			for (int i = 1; i < _comp.Count; i++)
			{
				_comp[0].EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				_comp[i].EmitAsAssignment(compiler, file, func, asmStackLevel, errors, this);
			}
		}
	}
}