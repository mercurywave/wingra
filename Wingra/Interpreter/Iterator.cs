using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Wingra.Interpreter
{
	class IteratorPointer : IReleaseMemory
	{
		internal IIterate _iterator;

		public void Initialize(IIterate iter)
		{
			_iterator = iter;
		}

		public int GenerationID { get; set; }

		public void Release(Malloc memory)
		{
			GenerationID++;
			memory.CheckIn(this);
			_iterator.Release(memory);
		}

		public Variable GetCurrent() => _iterator.Current;
		internal VariableList GetCurrentPacked() => _iterator.CurrentPacked;
		public bool IsComplete => _iterator.IsComplete;

		public void MoveNext(Job job, Malloc heap) => _iterator.MoveNext(job, heap);
	}

	class OCodeIterator : IIterate
	{
		Scope _scope;
		VariableList _return;
		internal bool _complete = false;
		public void Initialize(Scope scp, Malloc heap, int size)
		{
			_scope = scp;
			_return = null;
			_complete = false;
			_return = heap.CheckOutVTable(size);
		}

		public bool IsComplete => _complete;
		public Variable Current
		{
			get
			{
				if (_return.Count >= 1)
					return _return[0];
				throw new RuntimeException("did not yield any values");
			}
		}
		public VariableList CurrentPacked => _return;

		public void UpdateCurrent(List<Variable> list)
		{
			_return.Clear();
			_return.AddRange(list);
		}
		public void MoveNext(Job job, Malloc heap)
		{
			job.CallStack.Push(_scope);
		}

		public void Release(Malloc heap)
		{
			_scope.Destroy(heap, null);
			Job._bucketScope.CheckIn(_scope);
			for (int i = 0; i < _return.Count; i++)
				_return[i].Dispose(heap);
			heap.CheckIn(_return);
		}
	}

	class OObjIterator : IIterate
	{
		StructPointer _list;
		Variable _currKey;
		VariableList _ret = null;
		bool _owned;

		public void Initialize(Variable list, Malloc heap)
		{
			_list = list.GetStruct();
			_owned = !list.IsPointer;
			_currKey = _list.GetFirstKey(heap);
		}
		public bool IsComplete => !_currKey.HasValue;

		public Variable Current => _list.TryGetChild(_currKey) ?? new Variable();

		public VariableList CurrentPacked
		{
			get
			{
				// don't really need to worry about cleaning this up
				if (_ret == null) _ret = new VariableList(2);
				_ret[0] = Current;
				_ret[1] = _currKey;
				return _ret;
			}
		}

		public void MoveNext(Job job, Malloc heap)
		{
			_currKey = _list.GetNextKey(_currKey, heap);
		}
		public void Release(Malloc heap)
		{
			if (_owned) _list.Release(heap);
		}
	}

	interface IIterate
	{
		bool IsComplete { get; }
		Variable Current { get; }
		VariableList CurrentPacked { get; } // do not modify list
		void MoveNext(Job job, Malloc heap);
		void Release(Malloc heap);
	}
}
