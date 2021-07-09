using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Interpreter
{
	class Instruction
	{
		public Action<Job> FallBack;
		public Func<Job, Action<Job>> Optimizer = null;

		public Instruction(Action<Job> act) { FallBack = act; }
		public Instruction(Action<Job> fallback, Func<Job, Action<Job>> optimize)
		{
			FallBack = fallback;
			Optimizer = optimize;
		}
	}

	class OpPattern
	{
		public enum eType { Single, Repeat, Any }
		public eType Type = eType.Single;
		public eAsmCommand Match;
		public OpPattern Next = null;

		public OpPattern(eAsmCommand match) { Match = match; }

		public bool Matches(eAsmCommand cmd)
		{
			if (Match == cmd) return true;
			return (Type == eType.Any);
		}

		public static OpPattern operator +(OpPattern a, OpPattern b) => a.Next = b;
	}

	class OpChain
	{
		OpPattern pointer;

		public enum eMatch { Reject, Continue, Complete }
		public eMatch State = eMatch.Continue;

		public OpChain(OpPattern start) { pointer = start; }

		public eMatch PeekMatch(eAsmCommand cmd)
		{
			State = _PeekMatch(cmd);
			return State;
		}
		eMatch _PeekMatch(eAsmCommand cmd)
		{
			if (pointer == null) return eMatch.Complete;
			switch (pointer.Type)
			{
				case OpPattern.eType.Single:
					if (pointer.Matches(cmd))
					{
						pointer = pointer.Next;
						return eMatch.Continue;
					}
					return eMatch.Reject;

				case OpPattern.eType.Repeat:
					if (pointer.Matches(cmd))
						return eMatch.Continue;
					pointer = pointer.Next;
					if (pointer == null)
						return eMatch.Continue;
					return eMatch.Reject;

				case OpPattern.eType.Any:
					pointer = pointer.Next;
					return eMatch.Continue;

				default: throw new NotImplementedException();
			}
		}
	}

	class OpChainEvaluator
	{
		public OpChain Chain;
		public Func<InstructionContext, Instruction> Match;
		public OpChainEvaluator(OpPattern pattern, Func<InstructionContext, Instruction> match)
		{
			Chain = new OpChain(pattern);
			Match = match;
		}
	}
}
