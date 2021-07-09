using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Interpreter
{
	public class CodeBlock
	{
		public FileCodeInstance FileCode;
		internal Dictionary<string, int> _localVarIndex;
		internal HashSet<string> _assumedVariables;
		internal List<Instruction> InstructionMetadata = new List<Instruction>();
		internal List<Action<Job>> Instructions;
		public bool AllowInjection;
		internal CodeBlock(AssemblyCode code, FileCodeInstance fci = null)
		{
			FileCode = fci;
			AllowInjection = code.AllowInjection;

			List<OpCondenser> operationPlan = code.OperationPlan;

			foreach (var plan in operationPlan)
				InstructionMetadata.Add(plan.OpMaker(new InstructionContext(code, plan.Assembly, plan.StartingLine)));
			Instructions = InstructionMetadata.Select(m => m.FallBack).ToList();

			_localVarIndex = new Dictionary<string, int>();
			for (int i = 0; i < code.LocalVariables.Count; i++)
			{
				var name = code.LocalVariables[i];
				_localVarIndex[name] = i;
			}
			_assumedVariables = new HashSet<string>(code._assumedVariables);
		}

		public override string ToString()
		{
			if (FileCode == null) return "[unknown code]";
			return FileCode.Name + "::" + FileCode.FindFunctionName(this);
		}
	}

	class OpCondenser
	{
		public List<AssemblyCodeLine> Assembly;
		public Func<InstructionContext, Instruction> OpMaker;
		public int StartingLine;
		public OpCondenser(List<AssemblyCodeLine> asm, OpChainEvaluator eval, int startLine)
		{
			Assembly = asm;
			OpMaker = eval.Match;
			StartingLine = startLine;
		}
	}
}
