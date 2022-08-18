using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Parser
{
	class SAwait : SExpressionComponent, IDecompose
	{
		SExpressionComponent _exp;
		public SAwait(SExpressionComponent exp) { _exp = exp; }
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_exp.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _exp;
		}

		public void RequestDecompose(int numRequested)
			=> (_exp as IDecompose)?.RequestDecompose(numRequested);
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
