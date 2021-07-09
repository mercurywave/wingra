using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Interpreter
{
	class Malloc
	{
		MemoryBucketer<DList> _lists = new MemoryBucketer<DList>(c => new DList(c));
		MemoryBucketer<DObject> _dObjs = new MemoryBucketer<DObject>(c => new DObject(c));
		MemoryBucketer<DMixedStruct> _dMixed = new MemoryBucketer<DMixedStruct>(c => new DMixedStruct(c));
		MemoryBucketer<DQueue> _dQueues = new MemoryBucketer<DQueue>(c => new DQueue(c));
		public IStructure CheckOut(int cap, bool listLike, bool allInts, bool allStrings)
		{
			if (listLike) return _lists.CheckOut(cap);
			// PERF: sparse int index?
			if (allStrings) return _dObjs.CheckOut(cap);
			return _dMixed.CheckOut(cap);
		}
		public DList CheckOutList(int cap) => _lists.CheckOut(cap);
		public IStructure CheckOutStructForKey(Variable key)
		{
			//PERF: could consider int-keyed arrays based on key value
			if (key.IsInt) return _lists.CheckOut(4);
			return _dObjs.CheckOut(4);
		}
		public void CheckIn(DList list) => _lists.CheckIn(list);
		public void CheckIn(IStructure obj)
		{
			if (obj is DList) _lists.CheckIn(obj as DList);
			else if (obj is DObject) _dObjs.CheckIn(obj as DObject);
			else if (obj is DMixedStruct) _dMixed.CheckIn(obj as DMixedStruct);
			else if (obj is DQueue) _dQueues.CheckIn(obj as DQueue);
			else throw new NotImplementedException();
		}

		public DObject CheckOutDObject(int cap) => _dObjs.CheckOut(cap);
		public void CheckIn(DObject obj) => _dObjs.CheckIn(obj);

		public DMixedStruct CheckOutMixedStruct(int cap) => _dMixed.CheckOut(cap);
		public void CheckIn(DMixedStruct obj) => _dMixed.CheckIn(obj);
		public DQueue CheckOutQueue(int cap) => _dQueues.CheckOut(cap);
		public void CheckIn(DQueue obj) => _dQueues.CheckIn(obj);

		FastStack<StructPointer> _structs = new FastStack<StructPointer>();

		public void CheckIn(StructPointer pointer) => _structs.Push(pointer);
		public StructPointer CheckOutStruct()
		{
			if (_structs.IsEmpty) return new StructPointer();
			return _structs.Pop();
		}
		public StructPointer CheckOutStruct(IStructure list)
		{
			var pointer = CheckOutStruct();
			pointer.Reset(list);
			return pointer;
		}

		public IManageReference CheckOutDuplicate(IManageReference orig)
		{
			if (orig is StructPointer)
			{
				var st = orig as StructPointer;
				return CheckOutStruct(st.DeepCopy(this));
			}
			if (orig is ILambda)
				throw new RuntimeException("cannot copy lambda");
			if (orig is IIterate)
				throw new RuntimeException("cannot copy iterator");
			throw new NotImplementedException();
		}

		FastStack<LambdaPointer> _funcs = new FastStack<LambdaPointer>();
		public void CheckIn(LambdaPointer func) => _funcs.Push(func);
		public LambdaPointer CheckOutFuncPoint()
		{
			if (_funcs.IsEmpty) return new LambdaPointer();
			return _funcs.Pop();
		}

		FastStack<IteratorPointer> _iterators = new FastStack<IteratorPointer>();
		public void CheckIn(IteratorPointer func) => _iterators.Push(func);
		public IteratorPointer CheckOutIterator(IIterate iter)
		{
			var wrapper = CheckOutIterator();
			wrapper.Initialize(iter);
			return wrapper;
		}
		public IteratorPointer CheckOutIterator()
		{
			if (_iterators.IsEmpty) return new IteratorPointer();
			return _iterators.Pop();
		}

		FastStack<OObjIterator> _objIterators = new FastStack<OObjIterator>();
		public void CheckIn(OObjIterator iter) => _objIterators.Push(iter);
		public OObjIterator CheckOutStructIterator()
		{
			if (_objIterators.IsEmpty) return new OObjIterator();
			return _objIterators.Pop();
		}

		FastStack<OCodeIterator> _codeIterators = new FastStack<OCodeIterator>();
		public void CheckIn(OCodeIterator iter) => _codeIterators.Push(iter);
		public OCodeIterator CheckOutCodeIterator()
		{
			if (_codeIterators.IsEmpty) return new OCodeIterator();
			return _codeIterators.Pop();
		}

		MemoryBucketer<VariableList> _vTables = new MemoryBucketer<VariableList>(c => new VariableList(c));
		public VariableList CheckOutVTable(int cap)
		{
			// I mostly want to think about this as a blank array of variables,
			// but in a few cases I trim these list counts, which then later check outs don't expect
			// maybe I shouldn't call .Clear() on the lists, but I don't want to think about this that hard :)
			var list = _vTables.CheckOut(cap);
			while (list.Count < cap)
				list.Add(new Variable());
			return list;
		}
		public void CheckIn(VariableList table)
		{
			//TODO: only enable in debug mode
			for (int i = 0; i < table.Count; i++)
			{
				if (!table[i].IsClean)
					throw new RuntimeException("forgot to clean up variable list");
				table[i] = new Variable();
			}
			_vTables.CheckIn(table);
		}

	}

	class MemoryBucketer<T> where T : IHaveCapacity
	{
		FastStack<T>[] _buckets = new FastStack<T>[16];
		Func<int, T> _generator;

		public MemoryBucketer(Func<int, T> generator)
		{
			_generator = generator;
			for (int i = 0; i < _buckets.Length; i++)
				_buckets[i] = new FastStack<T>();
		}

		public T CheckOut(int cap)
		{
			var idx = SizeToBucketIdx(cap);
			if (idx >= _buckets.Length) // giant array, don't bother reusing
				return _generator(BucketToMaxSize(idx));
			var stack = _buckets[idx];
			if (stack.IsEmpty)
				return _generator(BucketToMaxSize(idx));
			return stack.Pop();
		}

		public void CheckIn(T dic)
		{
			var idx = SizeToBucketIdx(dic.Capacity);
			if (idx >= _buckets.Length) // giant array, don't bother reusing
				return;
			var stack = _buckets[idx];
			stack.Push(dic);
		}

		int SizeToBucketIdx(int count)
			=> log2ciel(max(count, 4) - 1) - 2;
		int BucketToMinSize(int idx)
			=> idx == 0 ? 0 : (pow2(idx + 1) + 1);
		int BucketToMaxSize(int idx)
			=> pow2(idx + 2);

		#region fast math
		static int log2ciel(int num)
		{
			int bits = 0;
			if (num > 32767)
			{
				num >>= 16;
				bits += 16;
			}
			if (num > 127)
			{
				num >>= 8;
				bits += 8;
			}
			if (num > 7)
			{
				num >>= 4;
				bits += 4;
			}
			if (num > 1)
			{
				num >>= 2;
				bits += 2;
			}
			if (num > 0)
			{
				bits++;
			}
			return bits;
		}
		static int max(int a, int b)
		{
			if (a < b) return b;
			return a;
		}
		static int pow2(int b)
		{
			int result = 1;
			for (int i = 0; i < b; i++)
				result *= 2;
			return result;
		}
		#endregion
	}
}
