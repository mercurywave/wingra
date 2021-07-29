using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	public class SyntaxNode
	{
		internal virtual void OnAddedToTree(ParseContext context) { }
		internal virtual void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent) { throw new NotImplementedException(); }

	}

	public class STopOfFile : SyntaxNode, IHaveChildScope
	{
		public bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			if (LineParser.TryParseFile(context, currLine, out node, out usedTokens))
				return true;
			if (LineParser.TryParseLibrary(context, currLine, out node, out usedTokens))
				return true;
			return LineParser.TryParseStatement(context, currLine, out node, out usedTokens);
		}

		List<SyntaxNode> _children = new List<SyntaxNode>();
		public void AddChild(SyntaxNode node)
		{
			_children.Add(node);
		}
		public void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors) => EmitAssembly(compiler, file, func, asmStackLevel, errors, null);
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			List<SyntaxNode> structures = new List<SyntaxNode>();
			List<SyntaxNode> initCode = new List<SyntaxNode>();

			foreach (var child in _children)
			{
				//if (child is STemplate || child is SfunctionDef)
				//	structures.Add(child);
				//else
				initCode.Add(child);
			}
			foreach (var child in structures)
				EmitChild(compiler, child, file, func, asmStackLevel, errors);
			foreach (var child in initCode)
				EmitChild(compiler, child, file, func, asmStackLevel, errors);
		}
		internal void EmitChild(Compiler compiler, SyntaxNode child, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors)
		{
			child.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
		}
		public IEnumerable<SyntaxNode> Children => _children;
	}

	internal class SFakeFile : SyntaxNode, IHaveChildScope // TODO: better name?
	{
		public bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return LineParser.TryParseStatement(context, currLine, out node, out usedTokens);
		}

		List<SyntaxNode> _children = new List<SyntaxNode>();
		public void AddChild(SyntaxNode node)
		{
			_children.Add(node);
		}
		public void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors)
			=> EmitAssembly(compiler, file, func, asmStackLevel, errors, null);
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			foreach (var child in _children)
				EmitChild(compiler, child, file, func, asmStackLevel, errors);
		}
		internal void EmitChild(Compiler compiler, SyntaxNode child, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors)
		{
			child.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
		}
		public IEnumerable<SyntaxNode> Children => _children;
	}
	class SyntaxScopeHelper
	{
		internal List<SyntaxNode> _children = new List<SyntaxNode>();
		public IEnumerable<SyntaxNode> Children => _children;
		public void Add(SyntaxNode node)
		{
			_children.Add(node);
		}
		public void EmitChildren(Compiler compiler, SyntaxNode node, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors)
		{
			foreach (var child in _children)
				child.EmitAssembly(compiler, file, func, asmStackLevel, errors, node);
		}
	}
	public class SStatement : SyntaxNode
	{
		public int FileLine;
		public SStatement(int fileLine) { FileLine = fileLine; }
		internal override sealed void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.SetFileLine(FileLine);
			try
			{
				_EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
			}
			catch (CompilerException ex)
			{
				var line = ex.Line;
				if (line < 0) line = FileLine;
				errors.LogError(ex.Message, line, ex.Token, ex.Type);
			}
		}
		internal override void OnAddedToTree(ParseContext context)
		{
			var nodeList = IterExpressionsRecursive().ToList();
			foreach (var exp in nodeList)
				exp.OnAddedToTree(context);
			base.OnAddedToTree(context);
		}
		internal virtual void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
		}

		public virtual IEnumerable<SExpressionComponent> IterExpressions()
		{
			return Enumerable.Empty<SExpressionComponent>();
		}

		public IEnumerable<SExpressionComponent> IterExpressionsRecursive()
		{
			var expressions = IterExpressions().ToArray();
			var expanded = new HashSet<SExpressionComponent>();
			foreach (var child in expressions)
				ExpandExpTree(expanded, child);
			return expanded;
		}
		static void ExpandExpTree(HashSet<SExpressionComponent> list, SExpressionComponent comp)
		{
			// PERF: I think I'm being a little lazy with this stuff
			if (comp == null) return;
			if (list.Contains(comp)) return;
			list.Add(comp);
			var curr = comp.IterExpChildren().ToList();
			foreach (var node in curr)
				ExpandExpTree(list, node);
		}
	}

	//simple scoped statments, where inner scope is just code
	public class SScopeStatement : SStatement, IHaveChildScope
	{
		public SScopeStatement(int fileLine) : base(fileLine) { }
		SyntaxScopeHelper _scope = new SyntaxScopeHelper();
		public IEnumerable<SyntaxNode> Children => _scope.Children;
		public virtual bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return LineParser.TryParseStatement(context, currLine, out node, out usedTokens);
		}
		public virtual void AddChild(SyntaxNode node)
		{
			_scope.Add(node);
		}
		protected void EmitChildren(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors)
		{
			_scope.EmitChildren(compiler, this, file, func, asmStackLevel, errors);
		}
		// anything that implements this must implement _EmitAssembly themselves and emit children as apporpriate
		// there are too many edge cases for this to try and mask the complexity
	}

	#region escape statements

	class SReturn : SStatement, ICanEmitInline
	{
		List<SExpressionComponent> _ret;
		public SReturn(int fileLine, List<SExpressionComponent> ret = null) : base(fileLine)
		{
			_ret = ret;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_ret == null)
			{
				if (func.HasDefinedReturns)
					throw new CompilerException("Function has defined returns, use 'quit' instead to exit the function", func.CurrentFileLine);
				func.InjectDeferrals(asmStackLevel);
				func.Add(asmStackLevel, eAsmCommand.Return);
			}
			else if (func.HasDefinedReturns)
			{
				if(func.DefinedReturnCount != _ret.Count)
					throw new CompilerException("Mismatch between number of output parameters and number of return expressions", func.CurrentFileLine);
				for (int i = 0; i < _ret.Count; i++)
				{
					var par = _ret[i];
					var ident = par as SIdentifier;
					var sym = func.GetReturnParamByIdx(i);
					if (ident == null || ident.Symbol != sym)
					{
						par.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
						func.Add(asmStackLevel, eAsmCommand.StoreLocal, sym);
					}
					// do nothing if the the expected output is 'a' and you return 'a'
					// we don't want to re-assign, as that would lose data ownership
				}
				func.InjectDeferrals(asmStackLevel);
				func.Add(asmStackLevel, eAsmCommand.Quit);
			}
			else
			{
				foreach (var par in _ret)
					par.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				func.InjectDeferrals(asmStackLevel);
				func.Add(asmStackLevel, eAsmCommand.Return, _ret.Count);
			}
		}

		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_ret.Count != 1) // should have caught this earlier
				throw new CompilerException("failed to inline function", compiler.FileLine);
			_ret[0].EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			return true;
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			return _ret;
		}
	}

	// special case for an arrow return on the first line of a function instead of on the same line
	class SArrowReturnStatement : SReturn
	{
		public SArrowReturnStatement(int fileLine, List<SExpressionComponent> ret = null) : base(fileLine, ret) { }
	}

	class SQuit : SStatement
	{
		public SQuit(int fileLine) : base(fileLine) { }
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.InjectDeferrals(asmStackLevel);
			func.Add(asmStackLevel, eAsmCommand.Quit);
		}
	}

	class SYield : SStatement
	{
		List<SExpressionComponent> _returns;
		public SYield(int fileline, List<SExpressionComponent> rets) : base(fileline)
		{
			_returns = rets;
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_returns == null || _returns.Count == 0)
				func.Add(asmStackLevel, eAsmCommand.YieldIterator); // Not splitting coroutine into a special case, unlike Kilt
			else
			{
				foreach (var par in _returns)
					par.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				func.Add(asmStackLevel, eAsmCommand.YieldIterator, _returns.Count);
			}
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			return _returns;
		}
	}

	class SDefer : SScopeStatement, IDefer
	{
		public SDefer(int fileLine) : base(fileLine) { }
		string Uniq;

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.RegisterDefer(this);
			Uniq = func.GetReserveUniqueTemp("**defer" + FileLine);
			func.Add(asmStackLevel, eAsmCommand.FlagDefer, Uniq);
		}
		public void EmitDefer(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.RunDeferIfSet, Uniq);
			EmitChildren(compiler, file, func, asmStackLevel + 1, errors);
		}
	}
	class SDeferStatement : SStatement, IDefer
	{
		List<SStatement> _statements;
		public SDeferStatement(int fileLine, List<SStatement> statements) : base(fileLine) { _statements = statements; }
		string Uniq;

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			foreach (var state in _statements)
				foreach (var sub in state.IterExpressions())
					yield return sub;
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.RegisterDefer(this);
			Uniq = func.GetReserveUniqueTemp("**defer" + FileLine);
			func.Add(asmStackLevel, eAsmCommand.FlagDefer, Uniq);
		}
		public void EmitDefer(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.RunDeferIfSet, Uniq);
			foreach (var child in _statements)
				child.EmitAssembly(compiler, file, func, asmStackLevel + 1, errors, this);
		}
	}
	#endregion


	interface IHaveChildScope
	{
		bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens);
		void AddChild(SyntaxNode node);
	}

	interface ICanBeProperty
	{
		void EmitPropertyAction(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent);
	}

	public interface IExportGlobalSymbol
	{
		IEnumerable<string> GetExportableSymbolsInside(SyntaxNode parent);
	}

	public interface IDeclareVariablesAtScope
	{
		IEnumerable<string> GetDeclaredSymbolsInside(SyntaxNode parent);
	}

	internal interface IDefer
	{
		void EmitDefer(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent);
	}
}
