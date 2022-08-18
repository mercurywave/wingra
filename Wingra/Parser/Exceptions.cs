using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	class STrapStatement : SScopeStatement, IDeclareVariablesAtScope
	{
		List<SStatement> _attempt;
		public STrapStatement(int fileLine, List<SStatement> attempt) : base(fileLine)
		{
			_attempt = attempt;
		}
		internal override void OnAddedToTree(ParseContext context)
		{
			foreach (var c in _attempt)
				c.OnAddedToTree(context);
			base.OnAddedToTree(context);
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.CreateErrorTrap, asmStackLevel + 1);
			foreach (var child in _attempt)
				child.EmitAssembly(compiler, file, func, asmStackLevel + 2, errors, this);
			// any vars declared at that level need to be hoisted to the current level so they can persist onward
			func.HoistDeclaredVars(asmStackLevel + 2, asmStackLevel);
			func.Add(asmStackLevel + 2, eAsmCommand.Jump, asmStackLevel);
			func.DeclareVariable("error", asmStackLevel + 1);
			func.Add(asmStackLevel + 1, eAsmCommand.ClearErrorTrap, func.FindParentErrorTrap());
			EmitChildren(compiler, file, func, asmStackLevel + 1, errors);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			foreach (var state in _attempt)
				foreach (var sub in state.IterExpressions())
					yield return sub;
		}

		public IEnumerable<string> GetDeclaredSymbolsInside(SyntaxNode parent)
		{
			foreach(var state in _attempt)
			{
				var decl = state as IDeclareVariablesAtScope;
				if (decl != null)
					foreach (var sub in decl.GetDeclaredSymbolsInside(this))
						yield return sub;
			}
		}
	}

	class STryExpression : SExpressionComponent, IDecompose
	{
		SExpressionComponent _try;
		SExpressionComponent _catch;
		public STryExpression(SExpressionComponent test, SExpressionComponent caught = null)
		{
			_try = test;
			_catch = caught;
		}

		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _try;
			if (_catch != null) yield return _catch;
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var saveToTemp = func.GetReserveUniqueTemp("opt");
			func.Add(asmStackLevel, eAsmCommand.CreateErrorTrap, asmStackLevel + 1);
			_try.EmitAssembly(compiler, file, func, asmStackLevel + 2, errors, this);
			func.Add(asmStackLevel + 2, eAsmCommand.StoreLocal, saveToTemp);
			func.Add(asmStackLevel + 2, eAsmCommand.Jump, asmStackLevel);
			func.DeclareVariable("error", asmStackLevel + 1);
			func.Add(asmStackLevel + 1, eAsmCommand.ClearErrorTrap, func.FindParentErrorTrap());
			if (_catch != null)
			{
				_catch.EmitAssembly(compiler, file, func, asmStackLevel + 1, errors, this);
				func.Add(asmStackLevel + 1, eAsmCommand.StoreLocal, saveToTemp);
			}
			else
			{
				func.InjectDeferrals(asmStackLevel + 1);
				func.Add(asmStackLevel + 1, eAsmCommand.Load, "error");
				func.Add(asmStackLevel + 1, eAsmCommand.ThrowError, 1);
			}
			func.Add(asmStackLevel, eAsmCommand.Load, saveToTemp);
		}

		public void RequestDecompose(int numRequested)
		{
			(_try as IDecompose)?.RequestDecompose(numRequested);
			(_catch as IDecompose)?.RequestDecompose(numRequested);
		}
	}

	class SAvowExpression : SExpressionComponent, IDecompose
	{
		SExpressionComponent _try;
		public SAvowExpression(SExpressionComponent test)
		{
			_try = test;
		}

		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _try;
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var saveToTemp = func.GetReserveUniqueTemp("opt");
			func.Add(asmStackLevel, eAsmCommand.CreateErrorTrap, asmStackLevel + 1);
			func.DeclareVariable("error", asmStackLevel + 1);
			_try.EmitAssembly(compiler, file, func, asmStackLevel + 2, errors, this);
			func.Add(asmStackLevel + 2, eAsmCommand.StoreLocal, saveToTemp);
			func.Add(asmStackLevel + 2, eAsmCommand.Jump, asmStackLevel);
			func.Add(asmStackLevel + 1, eAsmCommand.FatalError);
			func.Add(asmStackLevel, eAsmCommand.Load, saveToTemp);
		}
		public void RequestDecompose(int numRequested)
			=> (_try as IDecompose)?.RequestDecompose(numRequested);
	}

	class SThrowStatement : SStatement
	{
		SExpressionComponent _exp;
		public SThrowStatement(int fileLine, SExpressionComponent exp = null) : base(fileLine)
		{
			_exp = exp;
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			_exp?.OnAddedToTree(context);
			base.OnAddedToTree(context);
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.InjectDeferrals(asmStackLevel);
			_exp?.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			func.Add(asmStackLevel, eAsmCommand.ThrowError, _exp == null ? 0 : 1);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			if(_exp != null) yield return _exp;
		}
	}

	class SThrowExpression : SExpressionComponent
	{
		SExpressionComponent _exp;
		public SThrowExpression(SExpressionComponent exp = null) : base()
		{
			_exp = exp;
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			_exp?.OnAddedToTree(context);
			base.OnAddedToTree(context);
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.InjectDeferrals(asmStackLevel);
			_exp?.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			func.Add(asmStackLevel, eAsmCommand.ThrowError, _exp == null ? 0 : 1);
			func.Add(asmStackLevel, eAsmCommand.PushNull); //HACK: this is just here to make sure the I/O register totals work
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			if (_exp != null) yield return _exp;
		}
	}
}
