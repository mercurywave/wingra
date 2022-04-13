using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Parser
{
	class SForLoop : SScopeStatement, IDeclareVariablesAtScope
	{
		SExpressionComponent _loopVar;
		SExpressionComponent _init;
		SExpressionComponent _end; // optional
		SExpressionComponent _by;
		public SForLoop(int fileLine, SExpressionComponent loopVar, SExpressionComponent init, SExpressionComponent end, SExpressionComponent by = null) : base(fileLine)
		{
			_loopVar = loopVar;
			_init = init;
			_end = end;
			_by = by;
			// TODO: throw if 0
		}

		public IEnumerable<string> GetDeclaredSymbolsInside(SyntaxNode parent)
		{
			var iter = _loopVar as IHaveLocalIdentifierSymbol;
			if (iter != null)
				yield return iter.Symbol;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var localVar = _loopVar as IHaveLocalIdentifierSymbol;

			void SaveToIter(int stack, eAsmCommand cmd)
			{
				if (localVar != null)
					func.Add(stack, cmd, 0, localVar.Symbol);
				else if (_loopVar == null)
					func.Add(stack, cmd, 0, "it");
				else
					_loopVar.EmitAsAssignment(compiler, file, func, stack, errors, this);
			}

			void LoadIter(int stack)
			{
				if (localVar != null)
					func.Add(stack, eAsmCommand.Load, 0, localVar.Symbol);
				else if (_loopVar == null)
					func.Add(stack, eAsmCommand.Load, 0, "it");
				else
					_loopVar.EmitAssembly(compiler, file, func, stack, errors, this);
			}

			_init.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			eAsmCommand store;
			if (localVar is SIdentifier) store = eAsmCommand.StoreLocal;
			else store = eAsmCommand.StoreNewLocal;
			SaveToIter(asmStackLevel, store);

			var endVar = "";
			if (_end != null)
			{
				endVar = func.GetUniqueTemp("terminate");
				_end.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				func.Add(asmStackLevel, eAsmCommand.StoreNewLocal, endVar);
			}

			int? iBy = null;
			string uniqBy = null;
			if (_by == null) iBy = 1;
			if (_by is SLiteralNumber) iBy = (_by as SLiteralNumber).iValue;
			if (!iBy.HasValue) uniqBy = func.GetReserveUniqueTemp("by");
			if (uniqBy != null)
			{
				_by.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				func.Add(asmStackLevel, eAsmCommand.StoreNewLocal, uniqBy);
			}

			func.Add(asmStackLevel, eAsmCommand.LoopBegin, asmStackLevel + 2);

			// advance loop (skipped initially)
			LoadIter(asmStackLevel + 1);
			if (uniqBy != null)
				func.Add(asmStackLevel + 1, eAsmCommand.Load, uniqBy);
			else
				func.Add(asmStackLevel + 1, eAsmCommand.PushInt, iBy.Value);
			func.Add(asmStackLevel + 1, eAsmCommand.Add);
			SaveToIter(asmStackLevel + 1, eAsmCommand.StoreLocal);

			if (_end != null)
			{
				func.Add(asmStackLevel + 2, eAsmCommand.Load, 0, endVar);
				LoadIter(asmStackLevel + 2);
				if (uniqBy != null)
					func.Add(asmStackLevel + 2, eAsmCommand.ExceedInDirection, uniqBy);
				else
				{
					eAsmCommand comp = (iBy.Value > 0) ? eAsmCommand.GreaterThan : eAsmCommand.LessThan;
					func.Add(asmStackLevel + 2, comp);
				}
				func.Add(asmStackLevel + 2, eAsmCommand.LoopIfTest);
			}

			func.RegisterEscape(eToken.For, asmStackLevel, asmStackLevel);
			EmitChildren(compiler, file, func, asmStackLevel + 3, errors);
			func.Add(asmStackLevel + 3, eAsmCommand.Continue, asmStackLevel);
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			if (_loopVar != null) yield return _loopVar;
			if (_init != null) yield return _init;
			if (_end != null) yield return _end;
		}
	}
	// elements of the structure
	class SForOf : SScopeStatement
	{
		SExpressionComponent _structure;
		SExpressionComponent _iter; // optional
		SExpressionComponent _idx; // optional - for it in list at idx
		public SForOf(int fileLine, SExpressionComponent structure, SExpressionComponent iter = null, SExpressionComponent idx = null) : base(fileLine)
		{
			_structure = structure;
			_iter = iter;
			_idx = idx;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{

			string uniqName = "";
			string keyName = "";
			//if you want to loop over a structure that's the result of an expression, save the expression off
			if (!(_structure is SIdentifier))
			{
				uniqName = func.GetUniqueTemp("struct");
				_structure.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				func.Add(asmStackLevel, eAsmCommand.StoreNewLocal, uniqName);
			}
			if (_idx == null)
				keyName = func.GetUniqueTemp("key");

			void PushStruct(int lvl)
			{
				if (_structure is SIdentifier) _structure.EmitAssembly(compiler, file, func, lvl, errors, this);
				else func.Add(lvl, eAsmCommand.Load, uniqName);
			}
			void SaveKey(int lvl)
			{
				if (_idx == null) func.Add(lvl, eAsmCommand.StoreNewLocal, keyName);
				else _idx.EmitAsAssignment(compiler, file, func, lvl, errors, this);
			}
			void LoadKey(int lvl)
			{
				if (_idx == null) func.Add(lvl, eAsmCommand.Load, keyName);
				else _idx.EmitAssembly(compiler, file, func, lvl, errors, this);
			}

			// set up key iterator
			PushStruct(asmStackLevel);
			func.Add(asmStackLevel, eAsmCommand.LoadFirstKey);
			SaveKey(asmStackLevel);

			// begin loop
			func.Add(asmStackLevel, eAsmCommand.LoopBegin, asmStackLevel + 2);

			// advance loop (skipped initially)
			PushStruct(asmStackLevel + 1);
			LoadKey(asmStackLevel + 1);
			func.Add(asmStackLevel + 1, eAsmCommand.LoadNextKey);
			SaveKey(asmStackLevel + 1);

			// if not null, execute inner loop
			LoadKey(asmStackLevel + 2);
			func.Add(asmStackLevel + 2, eAsmCommand.HasValue);
			func.Add(asmStackLevel + 2, eAsmCommand.LoopIfTest);

			// save off current iterator value
			PushStruct(asmStackLevel + 3);
			LoadKey(asmStackLevel + 3);
			func.Add(asmStackLevel + 3, eAsmCommand.KeyAccess, 1);

			if (_iter == null) func.Add(asmStackLevel + 3, eAsmCommand.ReplaceOrNewLocal, "it");
			else _iter.EmitAsAssignment(compiler, file, func, asmStackLevel + 3, errors, this);

			// inner loop
			func.RegisterEscape(eToken.For, asmStackLevel, asmStackLevel);
			EmitChildren(compiler, file, func, asmStackLevel + 3, errors);
			func.Add(asmStackLevel + 3, eAsmCommand.Continue, asmStackLevel);
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _structure;
			if (_iter != null) yield return _iter;
			if (_idx != null) yield return _idx;
		}
	}
	// keys in the structure
	class SForIn : SScopeStatement
	{
		SExpressionComponent _structure;
		SExpressionComponent _idx; // optional - for it in list at idx
		public SForIn(int fileLine, SExpressionComponent structure, SExpressionComponent idx = null) : base(fileLine)
		{
			_structure = structure;
			_idx = idx;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			string uniqName = "";
			//if you want to loop over a structure that's the result of an expression, save the expression off
			if (!(_structure is SIdentifier))
			{
				uniqName = func.GetUniqueTemp("struct");
				_structure.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				func.Add(asmStackLevel, eAsmCommand.StoreNewLocal, uniqName);
			}
			void PushStruct(int lvl)
			{
				if (_structure is SIdentifier) _structure.EmitAssembly(compiler, file, func, lvl, errors, this);
				else func.Add(lvl, eAsmCommand.Load, uniqName);
			}
			void SaveKey(int lvl)
			{
				if (_idx == null) func.Add(lvl, eAsmCommand.ReplaceOrNewLocal, "it");
				else _idx.EmitAsAssignment(compiler, file, func, lvl, errors, this);
			}
			void LoadKey(int lvl)
			{
				if (_idx == null) func.Add(lvl, eAsmCommand.Load, "it");
				else _idx.EmitAssembly(compiler, file, func, lvl, errors, this);
			}

			// set up key iterator
			PushStruct(asmStackLevel);
			func.Add(asmStackLevel, eAsmCommand.LoadFirstKey);
			SaveKey(asmStackLevel);

			// begin loop
			func.Add(asmStackLevel, eAsmCommand.LoopBegin, asmStackLevel + 2);

			// advance loop (skipped initially)
			PushStruct(asmStackLevel + 1);
			LoadKey(asmStackLevel + 1);
			func.Add(asmStackLevel + 1, eAsmCommand.LoadNextKey);
			SaveKey(asmStackLevel + 1);

			// if not null, execute inner loop
			LoadKey(asmStackLevel + 2);
			func.Add(asmStackLevel + 2, eAsmCommand.HasValue);
			func.Add(asmStackLevel + 2, eAsmCommand.LoopIfTest);

			// inner loop
			func.RegisterEscape(eToken.For, asmStackLevel, asmStackLevel);
			EmitChildren(compiler, file, func, asmStackLevel + 3, errors);
			func.Add(asmStackLevel + 3, eAsmCommand.Continue, asmStackLevel);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _structure;
			if (_idx != null) yield return _idx;
		}
	}
	class SForIterate : SScopeStatement
	{
		List<SExpressionComponent> _loopVars; // optional
		SExpressionComponent _structure;
		public SForIterate(int fileLine, SExpressionComponent structure, List<SExpressionComponent> loopVars = null) : base(fileLine)
		{
			_loopVars = loopVars;
			_structure = structure;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var iterName = func.GetUniqueTemp("iter");
			if (_loopVars != null && _loopVars.Count > 1)
			{
				foreach (var ident in _loopVars)
					if (ident is SReserveIdentifierExp)
						func.DeclareVariable((ident as SReserveIdentifierExp).Symbol, asmStackLevel);
			}
			_structure.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			func.Add(asmStackLevel, eAsmCommand.IterCreate);
			func.Add(asmStackLevel, eAsmCommand.StoreNewLocal, iterName);

			func.Add(asmStackLevel, eAsmCommand.LoopBegin, asmStackLevel + 2);

			// advance loop (skipped initially)
			func.Add(asmStackLevel + 1, eAsmCommand.Load, iterName);
			func.Add(asmStackLevel + 1, eAsmCommand.IterMoveNext);

			func.Add(asmStackLevel + 2, eAsmCommand.Load, iterName);
			func.Add(asmStackLevel + 2, eAsmCommand.IterIsComplete);
			func.Add(asmStackLevel + 2, eAsmCommand.Not); // PERF: combine
			func.Add(asmStackLevel + 2, eAsmCommand.LoopIfTest);

			if (_loopVars == null || _loopVars.Count == 1)
			{
				func.Add(asmStackLevel + 3, eAsmCommand.Load, iterName);
				func.Add(asmStackLevel + 3, eAsmCommand.IterLoadCurrent);
				if (_loopVars == null)
					func.Add(asmStackLevel + 3, eAsmCommand.ReplaceOrNewLocal, "it");
				else
					_loopVars[0].EmitAsAssignment(compiler, file, func, asmStackLevel + 3, errors, this);
			}
			else
			{
				func.Add(asmStackLevel + 3, eAsmCommand.Load, iterName);
				func.Add(asmStackLevel + 3, eAsmCommand.IterLoadCurrPacked, _loopVars.Count);
				foreach (var child in _loopVars)
					child.EmitAsAssignment(compiler, file, func, asmStackLevel + 3, errors, this);
			}

			func.RegisterEscape(eToken.For, asmStackLevel, asmStackLevel);
			EmitChildren(compiler, file, func, asmStackLevel + 3, errors);
			func.Add(asmStackLevel + 3, eAsmCommand.Continue, asmStackLevel);
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _structure;
			if (_loopVars != null)
				foreach (var v in _loopVars)
					yield return v;
		}
	}

	class SWhile : SScopeStatement
	{
		SExpressionComponent _test;
		public SWhile(int fileLine, SExpressionComponent test) : base(fileLine)
		{
			_test = test;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.LoopBegin, asmStackLevel + 1);
			_test.EmitAssembly(compiler, file, func, asmStackLevel + 1, errors, this);
			func.Add(asmStackLevel + 1, eAsmCommand.LoopIfTest);

			func.RegisterEscape(eToken.While, asmStackLevel, asmStackLevel);
			EmitChildren(compiler, file, func, asmStackLevel + 3, errors);

			func.Add(asmStackLevel + 3, eAsmCommand.Continue, asmStackLevel);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _test;
		}
	}

	class SBreak : SStatement
	{
		public SBreak(int fileLine) : base(fileLine)
		{
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.Break, func.GetCurrentEscapeDepthBreak());
		}
	}
	class SContinue : SStatement
	{
		public SContinue(int fileLine) : base(fileLine)
		{
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.Continue, func.GetCurrentEscapeDepthContinue());
		}
	}
}
