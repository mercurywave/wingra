using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	class SIfStatment : SScopeStatement
	{
		SExpressionComponent _test;
		public SIfStatment(int fileLine, SExpressionComponent test) : base(fileLine)
		{
			_test = test;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var shortcut = _test.TryCompileTimeInlineBool(compiler, file, errors);
			if (shortcut == null)
			{
				func.ClearAsmStack(asmStackLevel);
				_test.EmitAssembly(compiler, file, func, asmStackLevel + 1, errors, this);
				func.Add(asmStackLevel + 1, eAsmCommand.DoIfTest);
				func.RegisterIfElse(asmStackLevel + 1);
				EmitChildren(compiler, file, func, asmStackLevel + 2, errors);
				func.Add(asmStackLevel + 2, eAsmCommand.Jump, asmStackLevel);
			}
			else if (shortcut.Value)
			{
				func.ClearAsmStack(asmStackLevel);
				func.RegisterIfElse(asmStackLevel + 1, true);
				EmitChildren(compiler, file, func, asmStackLevel + 2, errors);
				func.Add(asmStackLevel + 2, eAsmCommand.Jump, asmStackLevel);
			}
			else
			{
				func.ClearAsmStack(asmStackLevel);
				func.RegisterIfElse(asmStackLevel + 1);
			}
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _test;
		}
	}

	class SElseIfStatment : SScopeStatement
	{
		SExpressionComponent _test;
		public SElseIfStatment(int fileLine, SExpressionComponent test) : base(fileLine)
		{
			_test = test;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (!func.InIfElseScope(asmStackLevel + 1)) { errors.LogError("Elseif without preceeding if", ePhase.Emit, FileLine); return; }
			if (!func.IsBranchReachable(asmStackLevel + 1)) return;

			var shortcut = _test.TryCompileTimeInlineBool(compiler, file, errors);

			if (shortcut == null)
			{
				func.ClearAsmStack(asmStackLevel + 1);
				_test.EmitAssembly(compiler, file, func, asmStackLevel + 2, errors, this);
				func.Add(asmStackLevel + 1, eAsmCommand.DoIfTest);
				func.RegisterIfElse(asmStackLevel + 1);
				EmitChildren(compiler, file, func, asmStackLevel + 2, errors);
				func.Add(asmStackLevel + 2, eAsmCommand.Jump, asmStackLevel);
			}
			else if (shortcut.Value)
			{
				func.ClearAsmStack(asmStackLevel + 1);
				func.RegisterIfElse(asmStackLevel + 1, true);
				EmitChildren(compiler, file, func, asmStackLevel + 2, errors);
				func.Add(asmStackLevel + 2, eAsmCommand.Jump, asmStackLevel);
			}
			else
			{
				func.ClearAsmStack(asmStackLevel + 1);
				func.RegisterIfElse(asmStackLevel + 1);
			}
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _test;
		}
	}
	class SElseStatment : SScopeStatement
	{
		public SElseStatment(int fileLine) : base(fileLine) { }

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (!func.InIfElseScope(asmStackLevel + 1)) { errors.LogError("Else without preceeding if", ePhase.Emit, FileLine); return; }
			if (!func.IsBranchReachable(asmStackLevel + 1)) return;
			func.ClearAsmStack(asmStackLevel + 1);
			EmitChildren(compiler, file, func, asmStackLevel + 1, errors);
			//func.Add(asmStackLevel + 2, eAsmCommand.Break, asmStackLevel); // shouldn't be neccessary, code will auto-continue to the next line
		}
	}

	// switch
	// switch test
	class SSwitchStatement : SStatement, IHaveChildScope
	{
		SExpressionComponent _test; // may be null
		string _identifier = "";
		internal string TempIdent => _identifier;
		public SSwitchStatement(int fileLine, SExpressionComponent exp = null) : base(fileLine)
		{
			_test = exp;
		}

		public virtual bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			if (_test == null)
				return LineParser.TryParseSwitchIf(context, currLine, out node, out usedTokens);
			return LineParser.TryParseSwitchCase(context, currLine, out node, out usedTokens);
		}

		protected List<SyntaxNode> _children = new List<SyntaxNode>();
		public IEnumerable<SyntaxNode> Children => _children;
		public void AddChild(SyntaxNode node) => _children.Add(node);
		public void AddBlankLine() { }

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.ClearAsmStack(asmStackLevel);
			if (_test != null)
			{
				if (_test is SIdentifier)
					_identifier = (_test as SIdentifier).Symbol;
				else
				{
					_test.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
					_identifier = func.GetUniqueTemp("tmp");
					func.DeclareVariable(_identifier, asmStackLevel);
					func.Add(asmStackLevel, eAsmCommand.StoreLocal, _identifier);
				}
			}

			// I don't think we need to validate there is an else clause - nothing happens if not present
			foreach (var c in _children)
				c.EmitAssembly(compiler, file, func, asmStackLevel + 1, errors, this);
		}

		public static SExpressionComponent CombineListIntoOrExpression(SExpressionComponent test, List<SExpressionComponent> comparisons)
		{
			comparisons.Reverse(); // caution: not a copy,
			SExpressionComponent MakeNode(SExpressionComponent c) => test == null ? c : new SOperation(c, new SOperand(eToken.EqualSign), test);

			SExpressionComponent node = MakeNode(comparisons[0]);
			for (int i = 1; i < comparisons.Count; i++)
				node = new SOperation(node, new SOperand(eToken.Or), MakeNode(comparisons[i]));

			return node;
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			if (_test != null) yield return _test;
		}
	}

	// test \ execute; ...
	// else \ execute; ...
	class SSwitchCase : SScopeStatement
	{
		SExpressionComponent _test; // null in else case
		public SSwitchCase(int fileLine, SExpressionComponent test) : base(fileLine)
		{
			_test = test;
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var sw = parent as SSwitchStatement;
			if (_test != null)
			{
				func.ClearAsmStack(asmStackLevel);
				if (sw != null && sw.TempIdent != "")
				{
					func.Add(asmStackLevel, eAsmCommand.Load, sw.TempIdent);
					_test.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
					func.Add(asmStackLevel, eAsmCommand.Equals);
				}
				else
					_test.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				func.Add(asmStackLevel, eAsmCommand.DoIfTest);
				EmitChildren(compiler, file, func, asmStackLevel + 1, errors);
				func.Add(asmStackLevel + 1, eAsmCommand.Jump, asmStackLevel - 1);
			}
			else
			{
				func.ClearAsmStack(asmStackLevel); // certain inlining cases can bite us
				EmitChildren(compiler, file, func, asmStackLevel, errors);
			}
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			if (_test != null) yield return _test;
		}
	}

	// @a = switch
	// @a = switch test
	class SSwitchExpression : SExpressionComponent, IHaveChildScope
	{
		internal SExpressionComponent _test; // may be null
		string _identifier = "";
		internal string TempIdent => _identifier;
		internal string SaveToTemp;
		public SSwitchExpression(SExpressionComponent exp = null) : base()
		{
			_test = exp;
		}

		public virtual bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return LineParser.TryParseSwitchExpressionCase(context, currLine, out node, out usedTokens);
		}

		protected List<SSwitchExpressionCase> _children = new List<SSwitchExpressionCase>();
		public IEnumerable<SSwitchExpressionCase> Children => _children;
		public void AddBlankLine() { }
		public void AddChild(SyntaxNode node)
		{
			if (!(node is SSwitchExpressionCase)) throw new ParserException("unexpected node type"); // probably shouldn't ever be hit...
			_children.Add(node as SSwitchExpressionCase);
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (!_children.Any(s => s is SSwitchExpressionElse)) errors.LogError("switch expression has no else case", ePhase.Emit, compiler.FileLine);
			SaveToTemp = func.GetReserveUniqueTemp("sOut");
			func.ClearAsmStack(asmStackLevel);
			if (_test != null)
			{
				if (_test is IHaveLocalIdentifierSymbol)
					_identifier = (_test as IHaveLocalIdentifierSymbol).Symbol;
				else
				{
					_test.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
					_identifier = func.GetUniqueTemp("temp");
					func.DeclareVariable(_identifier, asmStackLevel);
					func.Add(asmStackLevel, eAsmCommand.StoreLocal, _identifier);
				}
			}
			foreach (var c in _children)
				c.EmitAssembly(compiler, file, func, asmStackLevel + 1, errors, this);
			func.Add(asmStackLevel, eAsmCommand.Load, SaveToTemp);
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			if (_test != null) yield return _test;
		}
	}

	class SSwitchExpressionElse : SSwitchExpressionCase
	{
		public SSwitchExpressionElse(int fileLine, SExpressionComponent value) : base(fileLine, null, value) { }
	}

	// comparison : value
	class SSwitchExpressionCase : SStatement
	{
		internal SExpressionComponent _test; // null in else case (alternative seems like a lot of code duplication or weird inheritence)
		SExpressionComponent _value;
		public SSwitchExpressionCase(int fileLine, SExpressionComponent test, SExpressionComponent value) : base(fileLine)
		{
			_test = test;
			_value = value;
			if (_value == null) throw new ParserException("switch case expression has no value expression");
		}
		internal bool IsElseCase => _test == null;

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var sw = parent as SSwitchExpression;
			if (_test != null) // case
			{
				func.ClearAsmStack(asmStackLevel);
				if (sw != null && sw.TempIdent != "")
				{
					func.Add(asmStackLevel, eAsmCommand.Load, sw.TempIdent);
					_test.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
					func.Add(asmStackLevel, eAsmCommand.Equals);
				}
				else
					_test.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);

				func.Add(asmStackLevel, eAsmCommand.DoIfTest);
				_value.EmitAssembly(compiler, file, func, asmStackLevel + 1, errors, this);
				func.Add(asmStackLevel + 1, eAsmCommand.StoreLocal, sw.SaveToTemp);
				func.Add(asmStackLevel + 1, eAsmCommand.Jump, asmStackLevel - 1);
			}
			else // default case
			{
				_value.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				func.Add(asmStackLevel + 1, eAsmCommand.StoreLocal, sw.SaveToTemp);
			}
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			if (_test != null) yield return _test;
			yield return _value;
		}
	}
}
