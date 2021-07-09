using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Parser
{
	class SAwait : SExpressionComponent, IWillDecompose
	{
		SExpressionComponent _exp;
		int _toDecompose = 1;
		public SAwait(SExpressionComponent exp) { _exp = exp; }
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			// this is pretty hacky. but this is the earliest we know what the value of this is
			// the alternatives are to pass thing through multiple layers, which is also gross
			if (parent is IWillDecompose)
				_toDecompose = (parent as IWillDecompose).NumToDecompose;
			_exp.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _exp;
		}
		public int NumToDecompose => _toDecompose;
	}

	class SArun : SExpressionComponent
	{
		SExpressionComponent _exp;
		public SArun(SExpressionComponent exp) { _exp = exp; }
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_exp is SLambda || _exp is SOneLiner || _exp is SLambdaMethod)
				_exp.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			else
			{
				var sub = file.GenLambda(func.CurrentFileLine);
				sub.IsAsync = true;
				sub.Add(0, eAsmCommand.IgnoreParams);
				if (_exp is ICanAwait) (_exp as ICanAwait).FlagAsAwaiting();
				_exp.EmitAssembly(compiler, file, sub, 0, errors, this);

				func.Add(asmStackLevel, eAsmCommand.CreateLambda, sub.UniqNameInFile);
			}
			func.Add(asmStackLevel, eAsmCommand.ARunCode);
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _exp;
		}
	}
}
