using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Interpreter
{
	class CommStack : List<Variable>
	{
		public CommStack() : base(32) { }
		int _processed = 0;
		// PERF: this does a full clear when it could act more like the FastStack
		//		 need to find everyone that uses .Count and make sure that can't be trusted
		public void QueueFromRegister(FastStack<Variable> registers, int count)
		{
			Empty();
			// the count is just a suggestion, if the caller was expecting something, it will explode
			if (count > registers.Depth)
				count = registers.Depth;
			registers.Kill(count);
			AddRange(registers.Dredge(0, count));
		}

		public void Empty()
		{
			Clear();
			_processed = 0;
		}

		public void Queue(Variable val)
		{
			Empty();
			Add(val);
		}
		public void Queue(params Variable[] vals)
		{
			Empty();
			AddRange(vals);
		}

		public Variable DequeueNext()
		{
			if (_processed >= Count) return new Variable();
			var ret = this[_processed++];
			if (_processed >= Count) Empty();
			return ret;
		}

		public int Remaining() => Count - _processed;

		public Variable DequeueRemaining(Malloc heap)
		{
			var list = heap.CheckOutList(Count - _processed);
			var pointer = heap.CheckOutStruct(list);
			for (int i = 0; i < Count - _processed; i++)
				list.TrySetChild(i, this[i + _processed], heap);
			Empty();
			return new Variable(pointer);
		}
		public List<Variable> DequeueRemainingList()
		{
			var list = new List<Variable>();
			for (int i = 0; i < Count - _processed; i++)
				list.Add(this[i + _processed]);
			Empty();
			return list;
		}
	}
}
