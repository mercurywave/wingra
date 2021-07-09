using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Interpreter
{
	class FastStack<T>
	{
		List<T> _list;
		int _count = 0;

		public FastStack(int capacity = 0)
		{
			_list = new List<T>(capacity);
		}

		public void Push(T val)
		{
			if (_count >= _list.Count)
			{
				_list.Add(val);
				_count++;
			}
			else _list[_count++] = val;
		}
		public void ReplaceTop(T val)
			=> _list[_count - 1] = val;
		// maintains order
		public void Push(List<T> list)
		{
			// I could _maybe_ optimize this, but there are a bunch of edge cases for not much benefit
			for (int i = 0; i < list.Count; i++)
				Push(list[list.Count - i - 1]);
		}

		public T Pop() => _list[--_count];
		public List<T> Pop(int count)
		{
			List<T> list = new List<T>(count);
			for (int i = 0; i < count; i++)
				list.Add(_list[_count - i - 1]);
			_count -= count;
			return list;
		}
		public List<T> PopReverse(int count)
		{
			List<T> list = new List<T>(count);
			for (int i = 0; i < count; i++)
				list.Add(_list[_count - count + i]);
			_count -= count;
			return list;
		}

		// silent pop X times
		public void Kill(int depth) => _count -= depth;

		public T Peek() => _list[_count - 1];
		public T Peek(int depth) => _list[_count - 1 - depth];

		public void Clear() => _count = 0;

		#region perf hacks

		// peek into what has been popped
		// this is super dicey, but saves a bunch of copies during function calls
		public T Dredge(int depth) => _list[_count + depth];
		public List<T> Dredge(int depth, int count) => _list.GetRange(_count + depth, count);

		// shove stuff back into no-mans land
		public void Mill(params T[] toDiscard)
		{
			var pointer = _count;
			foreach (var val in toDiscard)
			{
				if (pointer >= _list.Count)
				{
					_list.Add(val);
					pointer++;
				}
				else _list[pointer++] = val;
			}
		}

		// insert something at the far end of the stack
		public void PostPend(T farAWay)
		{
			_list.Insert(0, farAWay);
			_count++;
		}

		public List<T> MultiPeek(int depth)
			=> _list.GetRange(_count - depth, depth);

		public List<T> MultiPeekAll()
			=> MultiPeek(Depth);
		#endregion

		public int Depth => _count;
		public bool IsEmpty => _count == 0;

		public T PeekFromBottom(int idx) => _list[idx];

		public List<T> ToList() => _list.GetRange(0, _count);
	}
}
