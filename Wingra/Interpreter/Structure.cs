using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;

namespace Wingra.Interpreter
{
	class StructPointer : IManageReference
	{
		public enum eKeyType { Int, String, Var, Queue }
		IStructure Contents;

		public int Count => Contents.Count;
		public void Reset(IStructure content) { Contents = content; }

		public int GenerationID { get; set; }
		public void Release(Malloc memory)
		{
			GenerationID++;
			Contents.Release(memory);
			memory.CheckIn(Contents);
		}

		public void SetChild(Variable key, Variable value, Malloc heap)
		{
			Debug.Assert(!value.IsDisposed);
			if (key.IsInt)
				SetChild(key.AsInt(), value, heap);
			else if (key.IsString)
				SetChild(key.AsString(), value, heap);
			else
			{
				var result = Contents.TrySetChild(key, value, heap);
				if (result == eAddResult.Success) return;
				if (!IsKeyValid(key))
					throw new RuntimeException("Invalid key " + key.ToString());
				// no need to handle size increase specifically, already most generic type
				SwitchStructure(eKeyType.Var, heap, Contents.Capacity + 1);
				var final = Contents.TrySetChild(key, value, heap);
				Debug.Assert(final == eAddResult.Success);
			}
		}
		public void SetChild(string key, Variable value, Malloc heap)
		{
			Debug.Assert(!value.IsDisposed);
			var result = Contents.TrySetChild(key, value, heap);
			if (result == eAddResult.Success) return;
			if (key == "") throw new RuntimeException("Invalid key \"\"");
			AdjustStructure(eKeyType.String, result, heap);
			result = Contents.TrySetChild(key, value, heap);
			Debug.Assert(result == eAddResult.Success);
		}
		public void SetChild(int key, Variable value, Malloc heap)
		{
			Debug.Assert(!value.IsDisposed);
			var result = Contents.TrySetChild(key, value, heap);
			if (result == eAddResult.Success) return;
			if (result == eAddResult.NoSpace)
				AdjustStructure(CurrType(), result, heap);
			else
				AdjustStructure(eKeyType.Var, result, heap);
			var final = Contents.TrySetChild(key, value, heap);
			Debug.Assert(final == eAddResult.Success);
		}
		static internal bool IsKeyValid(Variable key)
		{
			if (key.IsNumeric) return true;
			if (key.IsString && key.AsString() != "") return true;
			if (key.IsBool) return true;
			if (key.IsPointer) return true;
			if (key.IsEnum) return true;
			return false;
		}
		eKeyType GetCompatibleType(eKeyType addType)
		{
			var curr = CurrType();
			if (curr == eKeyType.String && addType == eKeyType.String)
				return eKeyType.String;
			if (curr == eKeyType.Int && addType == eKeyType.Int)
				return eKeyType.Int;
			return eKeyType.Var;
		}

		bool IsCompatible(eKeyType addType)
			=> CurrType() == GetCompatibleType(addType);

		public void OptimizeStructure(eKeyType type, Malloc heap)
		{
			if (type == eKeyType.Queue && CurrType() == eKeyType.Int)
				SwitchStructure(eKeyType.Queue, heap, Contents.Capacity);
		}

		void AdjustStructure(eKeyType targetType, eAddResult result, Malloc heap)
		{
			if (result == eAddResult.NoSpace)
				MakeLarger(targetType, heap);
			else if (Count == 0)
			{
				if (result == eAddResult.Incompatible && targetType == eKeyType.Int)
					// if you have an empty list dim and set a[5]=3, that will need a var type
					SwitchStructure(eKeyType.Var, heap, 1);
				else
					// this is a special case for where we build dim without defining keys immediately
					// don't want to assume we need the overhead of var if it's more like a class
					SwitchStructure(targetType, heap, 1);
			}
			else
				SwitchStructure(eKeyType.Var, heap, Contents.Capacity + 1);
		}
		void MakeLarger(eKeyType targetType, Malloc heap)
			=> SwitchStructure(targetType, heap, Contents.Capacity + 1);
		void SwitchStructure(eKeyType targetType, Malloc heap, int capacity)
		{
			IStructure next;
			if (targetType == eKeyType.Int) next = heap.CheckOutList(capacity);
			else if (targetType == eKeyType.String) next = heap.CheckOutDObject(capacity);
			else next = heap.CheckOutMixedStruct(capacity);

			var copy = Contents.IterateUnordered();
			next.Fill(copy);
			Contents.Dispose();
			heap.CheckIn(Contents);
			Contents = next;
		}


		public Variable? TryGetChild(Variable key)
			=> Contents.GetChild(key);
		public Variable? TryGetChild(string key)
			=> Contents.GetChild(key);
		public Variable? TryGetChild(int index)
			=> Contents.GetChild(index);

		public void DeleteChild(Malloc memory, Variable key)
		{
			Contents.DeleteChild(key, memory);
		}
		public Variable DeletePopChild(Variable key)
			=> Contents.DeletePopChild(key);

		public IEnumerable<KeyValuePair<Variable, Variable>> IterateUnordered()
			=> Contents.IterateUnordered();
		public IEnumerable<KeyValuePair<Variable, Variable>> IterateOrdered()
			=> Contents.IterateOrdered();

		eKeyType CurrType()
		{
			if (Contents is DList) return eKeyType.Int;
			if (Contents is DObject) return eKeyType.String;
			//if (Contents is ) //TODO:
			return eKeyType.Var;
		}
		public Variable GetFirstKey(Malloc heap) => Contents.GetFirstKey(heap);
		public Variable GetLastKey(Malloc heap) => Contents.GetLastKey(heap);
		public Variable GetNextKey(Variable key, Malloc heap) => Contents.GetNextKey(key, heap);
		public Variable GetPrevKey(Variable key, Malloc heap) => Contents.GetPrevKey(key, heap);

		public IStructure DeepCopy(Malloc heap)
		{
			var next = CheckOutType(heap, CurrType(), Contents.Capacity);

			var copy = Contents.IterateUnordered();
			next.Fill(copy.Select(p => new KeyValuePair<Variable, Variable>(p.Key, p.Value.DeepCopy(heap, false))));
			return next;
		}
		public IStructure ShallowCopy(Malloc heap)
		{
			var next = CheckOutType(heap, CurrType(), Contents.Capacity);

			var copy = Contents.IterateUnordered();
			next.Fill(copy.Select(p => new KeyValuePair<Variable, Variable>(p.Key, p.Value.DuplicateAsRef())));
			return next;
		}
		IStructure CheckOutType(Malloc heap, eKeyType targetType, int capacity)
		{
			if (targetType == eKeyType.Int) return heap.CheckOutList(capacity);
			else if (targetType == eKeyType.String) return heap.CheckOutDObject(capacity);
			else return heap.CheckOutMixedStruct(capacity);
		}
	}

	class EnumPointer : IManageReference
	{
		public int GenerationID { get => 0; set { } }
		public Variable Contents;

		public EnumPointer(Variable contents)
		{
			Contents = contents;
		}

		public void Release(Malloc memory)
		{
		}
	}

	// no space means just expand this one
	// incompatible means it will never fit
	enum eAddResult { Success, NoSpace, Incompatible };
	class DList : IStructure
	{
		Variable[] _list;
		public DList(int capacity) { _list = new Variable[capacity]; }
		public int Capacity => _list.Length;
		int _count = 0;
		int _highWater = -1; // -1 means unknown

		public int Count => _count;

		public Variable? GetChild(Variable key)
		{
			if (!key.IsInt) return null;
			return GetChild(key.AsInt());
		}
		public Variable? GetChild(string key) => null; // not doing any auto-convert

		public Variable? GetChild(int index)
		{
			if (index < 0 || index >= Capacity) return null;
			var elem = _list[index];
			if (!elem.IsDisposed) return elem;
			return null;
		}

		public bool HasKey(Variable key)
			=> GetChild(key) != null;

		public eAddResult TrySetChild(Variable key, Variable value, Malloc memory)
		{
			if (key.IsInt) return TrySetChild(key.AsInt(), value, memory);
			return eAddResult.Incompatible;
		}

		public eAddResult TrySetChild(string key, Variable value, Malloc memory)
		{
			return eAddResult.Incompatible;
		}

		public eAddResult TrySetChild(int index, Variable value, Malloc memory)
		{
			if (index < 0) return eAddResult.Incompatible;
			if (index >= Capacity)
			{
				if (index < Capacity + 4 && Count > Capacity - 4) return eAddResult.NoSpace;
				return eAddResult.Incompatible;
			}
			if (!_list[index].IsDisposed) _list[index].Dispose(memory);
			else
			{
				_count++;
				if (index > _highWater) _highWater = index;
			}
			_list[index] = value.DuplicateRaw();
			return eAddResult.Success;
		}

		public void DeleteChild(Variable key, Malloc memory)
		{
			if (!key.IsInt) return;
			var idx = key.AsInt();
			if (idx < 0 || idx >= Capacity) return;
			if (_list[idx].IsDisposed) return;
			_count--;
			_highWater = -1;
			_list[idx].Dispose(memory);
		}
		public Variable DeletePopChild(Variable key)
		{
			if (!key.IsInt) return new Variable();
			var idx = key.AsInt();
			if (idx < 0 || idx >= Capacity) return new Variable();
			if (_list[idx].IsDisposed) return new Variable();
			_count--;
			_highWater = -1;
			var obj = _list[idx];
			_list[idx] = new Variable();
			return obj;
		}

		public IEnumerable<KeyValuePair<Variable, Variable>> IterateUnordered()
			=> IterateOrdered();
		public IEnumerable<KeyValuePair<Variable, Variable>> IterateOrdered()
		{
			for (int i = 0; i < Capacity; i++)
			{
				var elem = _list[i];
				if (!elem.IsDisposed)
				{
					var key = new Variable(i);
					yield return new KeyValuePair<Variable, Variable>(key, elem);
				}
			}
		}

		public void Fill(IEnumerable<KeyValuePair<Variable, Variable>> original)
		{
			foreach (var pair in original)
			{
				_list[pair.Key.AsInt()] = pair.Value;
				_count++;
			}
		}
		public void Dispose()
		{
			for (int i = 0; i < _list.Length; i++)
				_list[i].FlagDisposed();
			_count = 0;
			_highWater = -1;
		}

		public void Release(Malloc heap)
		{
			for (int i = 0; i < _list.Length; i++)
				_list[i].Dispose(heap);
			_count = 0;
			_highWater = -1;
		}

		public Variable GetFirstKey(Malloc heap)
			=> FindKey(-1);

		public Variable GetLastKey(Malloc heap)
		{
			if (_count == 0) return new Variable();
			if (_highWater >= 0) return new Variable(_highWater);
			for (int _highWater = Capacity - 1; _highWater >= 0; _highWater--)
				if (!_list[_highWater].IsDisposed)
					return new Variable(_highWater);
			throw new Exception(); // shouldn't be possible
		}

		public Variable GetNextKey(Variable key, Malloc heap)
		{
			if (_count == 0) return Variable.NULL;
			if (!key.HasValue) return GetFirstKey(heap);
			if (!key.IsInt) throw new NotImplementedException();
			return FindKey(key.AsInt());
		}
		Variable FindKey(int from)
		{
			for (int i = from + 1; i < Capacity; i++)
				if (!_list[i].IsDisposed)
					return new Variable(i);
			return Variable.NULL;
		}
		public Variable GetPrevKey(Variable key, Malloc heap)
		{
			if (_count == 0) return Variable.NULL;
			if (!key.HasValue) return GetLastKey(heap);
			if (!key.IsInt) throw new NotImplementedException();
			return FindKeyPrev(key.AsInt());
		}

		Variable FindKeyPrev(int from)
		{
			for (int i = from - 1; i >= 0; i--)
				if (!_list[i].IsDisposed)
					return new Variable(i);
			return Variable.NULL;
		}
	}
	class DObject : IStructure
	{
		Dictionary<string, Variable> _dict;
		int _cap;
		List<string> _sorted;
		bool _dirty = true;

		public DObject(int cap)
		{
			_dict = new Dictionary<string, Variable>(cap);
			_sorted = new List<string>(cap);
			_cap = cap;
		}
		public int Count => _dict.Count;
		public int Capacity => _cap;

		public eAddResult TrySetChild(Variable key, Variable value, Malloc heap)
		{
			if (key.IsString) return TrySetChild(key.AsString(), value, heap);
			return eAddResult.Incompatible;
		}

		public eAddResult TrySetChild(string key, Variable value, Malloc heap)
		{
			if (key == "") return eAddResult.Incompatible;
			if (_dict.ContainsKey(key))
				_dict[key].Dispose(heap);
			else if (Count == Capacity)
				return eAddResult.NoSpace;
			else // new key
				_dirty = true;
			_dict[key] = value.DuplicateRaw();
			return eAddResult.Success;
		}

		public eAddResult TrySetChild(int index, Variable value, Malloc heap)
		{
			return eAddResult.Incompatible;
		}
		public void Dispose()
		{
			foreach (var pair in _dict)
				pair.Value.FlagDisposed();
			_dirty = true;
			_sorted.Clear();
			_dict.Clear();
		}

		public void Release(Malloc heap)
		{
			foreach (var pair in _dict)
				pair.Value.Dispose(heap);
			_dirty = true;
			_sorted.Clear();
			_dict.Clear();
		}

		public Variable? GetChild(Variable key)
		{
			if (key.IsString) return GetChild(key.AsString());
			return null;
		}

		public Variable? GetChild(string key)
		{
			if (_dict.ContainsKey(key))
				return _dict[key];
			return null;
		}

		public Variable? GetChild(int index) => null;
		public void DeleteChild(Variable key, Malloc memory)
		{
			if (!key.IsString) return;
			var k = key.AsString();
			if (!_dict.ContainsKey(k)) return;
			_dirty = true;
			_dict[k].Dispose(memory);
			_dict.Remove(k);
		}
		public Variable DeletePopChild(Variable key)
		{
			if (!key.IsString) return new Variable();
			var k = key.AsString();
			if (!_dict.ContainsKey(k)) return new Variable();
			_dirty = true;
			var obj = _dict[k];
			_dict.Remove(k);
			return obj;
		}

		void BuildSorted()
		{
			if (!_dirty) return;
			_sorted.Clear();
			var keys = _dict.Keys.ToList();
			keys.Sort();
			_sorted.AddRange(keys);
		}
		public Variable GetFirstKey(Malloc heap)
		{
			if (Count == 0) return new Variable();
			BuildSorted();
			return new Variable(_sorted[0]);
		}

		public Variable GetLastKey(Malloc heap)
		{
			if (Count == 0) return new Variable();
			BuildSorted();
			return new Variable(_sorted[_sorted.Count - 1]);
		}

		public Variable GetNextKey(Variable key, Malloc heap)
		{
			if (Count == 0) return new Variable();
			if (!key.HasValue) return GetFirstKey(heap);
			if (!key.IsString) throw new RuntimeException("key is not a string");
			BuildSorted();
			var curr = _sorted.BinarySearch(key.AsString());
			curr = SearchIndexToRealIndex(curr, true, Count);
			if (curr < 0) return Variable.NULL;
			return new Variable(_sorted[curr]);
		}
		public Variable GetPrevKey(Variable key, Malloc heap)
		{
			if (Count == 0) return new Variable();
			if (!key.HasValue) return GetLastKey(heap);
			if (!key.IsString) throw new RuntimeException("key is not a string");
			BuildSorted();
			var curr = _sorted.BinarySearch(key.AsString());
			curr = SearchIndexToRealIndex(curr, false, Count);
			if (curr < 0) return Variable.NULL;
			return new Variable(_sorted[curr]);
		}
		public static int SearchIndexToRealIndex(int searchResult, bool loopingForward, int count)
		{
			if (loopingForward)
			{
				if (searchResult < 0) // iterator was removed along the way
				{
					if (~searchResult >= count) return -1; // reached end
					return ~searchResult; // ~ gets bitwise compliment
				}
				if (searchResult + 1 >= count) return -1;
				return searchResult + 1;
			}
			if (searchResult < 0) // iterator was removed along the way
			{
				if (~searchResult <= 0) return -1; // end
				if (~searchResult >= count) return count - 1;
				return (~searchResult) - 1; // ~ gets bitwise compliment
			}
			if (searchResult <= 0) return -1; // reached end
			return searchResult - 1;
		}

		public bool HasKey(Variable key)
		{
			if (!key.IsString) return false;
			return _dict.ContainsKey(key.AsString());
		}

		public IEnumerable<KeyValuePair<Variable, Variable>> IterateOrdered()
		{
			BuildSorted();
			foreach (var k in _sorted)
				yield return new KeyValuePair<Variable, Variable>(new Variable(k), _dict[k]);
		}

		public IEnumerable<KeyValuePair<Variable, Variable>> IterateUnordered()
		{
			foreach (var k in _dict.Keys)
				yield return new KeyValuePair<Variable, Variable>(new Variable(k), _dict[k]);
		}
		public void Fill(IEnumerable<KeyValuePair<Variable, Variable>> original)
		{
			_dict.Clear(); // are these neccessary?
			_dirty = true;
			foreach (var pair in original)
				_dict.Add(pair.Key.AsString(), pair.Value);
		}
	}

	class DMixedStruct : IStructure
	{
		Dictionary<Variable, Variable> _dict;
		int _cap;
		List<Variable> _sorted;
		bool _dirty = true;

		public DMixedStruct(int cap)
		{
			_dict = new Dictionary<Variable, Variable>(cap);
			_sorted = new List<Variable>(cap);
			_cap = cap;
		}
		public int Count => _dict.Count;
		public int Capacity => _cap;

		public eAddResult TrySetChild(Variable key, Variable value, Malloc heap)
		{
			if (StructPointer.IsKeyValid(key))
			{
				if (_dict.ContainsKey(key))
					_dict[key].Dispose(heap);
				else if (Count == Capacity)
					return eAddResult.NoSpace;
				else // new key
					_dirty = true;
				_dict[key] = value.DuplicateRaw();
				return eAddResult.Success;
			}
			return eAddResult.Incompatible;
		}

		public eAddResult TrySetChild(string key, Variable value, Malloc heap)
			=> TrySetChild(new Variable(key), value, heap);

		public eAddResult TrySetChild(int index, Variable value, Malloc heap)
			=> TrySetChild(new Variable(index), value, heap);

		public void Dispose()
		{
			foreach (var pair in _dict)
				pair.Value.FlagDisposed();
			_dirty = true;
			_sorted.Clear();
			_dict.Clear();
		}

		public void Release(Malloc heap)
		{
			foreach (var pair in _dict)
				pair.Value.Dispose(heap);
			_dirty = true;
			_sorted.Clear();
			_dict.Clear();
		}

		public Variable? GetChild(Variable key)
		{
			if (_dict.ContainsKey(key))
				return _dict[key];
			return null;
		}

		public Variable? GetChild(string key)
			 => GetChild(new Variable(key));

		public Variable? GetChild(int index)
			 => GetChild(new Variable(index));
		public void DeleteChild(Variable key, Malloc memory)
		{
			if (!_dict.ContainsKey(key)) return;
			_dirty = true;
			_dict[key].Dispose(memory);
			_dict.Remove(key);
		}
		public Variable DeletePopChild(Variable key)
		{
			if (!_dict.ContainsKey(key)) return new Variable();
			_dirty = true;
			var obj = _dict[key];
			_dict.Remove(key);
			return obj;
		}

		void BuildSorted()
		{
			if (!_dirty) return;
			_sorted.Clear();
			var keys = _dict.Keys.ToList();
			keys.Sort();
			_sorted.AddRange(keys);
			_dirty = false;
		}
		public Variable GetFirstKey(Malloc heap)
		{
			if (Count == 0) return new Variable();
			BuildSorted();
			return _sorted[0];
		}

		public Variable GetLastKey(Malloc heap)
		{
			if (Count == 0) return new Variable();
			BuildSorted();
			return _sorted[_sorted.Count - 1];
		}

		public Variable GetNextKey(Variable key, Malloc heap)
		{
			if (Count == 0) return new Variable();
			if (!key.HasValue) return GetFirstKey(heap);
			BuildSorted();
			var curr = _sorted.BinarySearch(key);
			curr = DObject.SearchIndexToRealIndex(curr, true, Count);
			if (curr < 0) return Variable.NULL;
			return _sorted[curr];
		}
		public Variable GetPrevKey(Variable key, Malloc heap)
		{
			if (Count == 0) return new Variable();
			if (!key.HasValue) return GetLastKey(heap);
			BuildSorted();
			var curr = _sorted.BinarySearch(key);
			curr = DObject.SearchIndexToRealIndex(curr, false, Count);
			if (curr < 0) return Variable.NULL;
			return _sorted[curr];
		}

		public bool HasKey(Variable key)
		{
			if (!key.IsString) return false;
			return _dict.ContainsKey(key);
		}

		public IEnumerable<KeyValuePair<Variable, Variable>> IterateOrdered()
		{
			BuildSorted();
			foreach (var k in _sorted)
				yield return new KeyValuePair<Variable, Variable>(k, _dict[k]);
		}

		public IEnumerable<KeyValuePair<Variable, Variable>> IterateUnordered()
		{
			foreach (var k in _dict.Keys)
				yield return new KeyValuePair<Variable, Variable>(k, _dict[k]);
		}
		public void Fill(IEnumerable<KeyValuePair<Variable, Variable>> original)
		{
			_dict.Clear(); // are these neccessary?
			_dirty = true;
			foreach (var pair in original)
				_dict.Add(pair.Key, pair.Value);
		}
	}

	class DQueue : IStructure
	{
		FastCappedQueue<Variable> _list;
		// the queue doesn't really handle blanks, it seems unlikely there would be any,
		// but I don't want to build a system to reject a delete and switch the structure
		// does mean that there are unlikely blanks on dequeue/pop that need to be skipped
		int _blanks;
		public DQueue(int cap)
		{
			_list = new FastCappedQueue<Variable>(cap);
			_blanks = 0;
		}
		public int Count => _list.Count;

		public int Capacity => _list.Capacity;

		public void DeleteChild(Variable key, Malloc memory)
		{
			var curr = DeletePopChild(key);
			curr.Dispose(memory);
		}

		public Variable DeletePopChild(Variable key)
		{
			if (!key.IsInt) return Variable.NULL;
			var idx = key.AsInt();
			if (Count == 0) return Variable.NULL;
			if (idx == _list.FirstIdx)
			{
				var curr = _list.Dequeue();
				if (!curr.IsDisposed)
					return curr;
				_blanks--;
				return Variable.NULL;
			}
			else if (idx == _list.LastIdx)
			{
				var curr = _list.Pop();
				if (!curr.IsDisposed)
					return curr;
				_blanks--;
				return Variable.NULL;
			}
			else
			{
				if (!_list.IsIdxInRange(idx)) return Variable.NULL;
				var curr = _list.Get(idx);
				if (curr.IsDisposed) return Variable.NULL;
				_blanks++;
				_list.Set(idx, Variable.DISPOSED);
				return curr;
			}
		}

		public void Dispose()
		{
			while (_list.Count > 0)
			{
				var curr = _list.Dequeue();
				// curr.FlagDisposed(); //  this doesn't do anything, we have a copy
			}
			_blanks = 0;
		}

		public void Fill(IEnumerable<KeyValuePair<Variable, Variable>> original)
		{
			// this could end very poorly if the caller isn't extremely careful
			var list = original.ToList();
			list.Sort((a, b) => a.Key.CompareTo(b.Key));
			var start = 0;
			var last = 0;
			if (list.Count > 0)
			{
				start = list[0].Key.AsInt();
				last = list[list.Count - 1].Key.AsInt();
			}
			_list.ResetFromIndex(start, last - start + 1);
			for (int i = start; i < last - start + 1; i++)
				_list.Set(i, Variable.DISPOSED);
			foreach (var pair in list)
				_list.Set(pair.Key.AsInt(), pair.Value);
		}

		public Variable? GetChild(Variable key)
		{
			if (!key.IsInt) return null;
			return GetChild(key.AsInt());
		}

		public Variable? GetChild(string key)
			=> null;

		public Variable? GetChild(int index)
		{
			if (!_list.IsIdxInRange(index))
				return null;
			var curr = _list.Get(index);
			if (curr.IsDisposed) return null;
			return curr;
		}

		public Variable GetFirstKey(Malloc heap)
		{
			if (Count == 0) return Variable.NULL;
			while (_list.Count > 0)
			{
				var first = _list.Get(_list.FirstIdx);
				if (!first.IsDisposed) return new Variable(_list.FirstIdx);
				_list.Dequeue();
				_blanks--;
			}
			throw new Exception("blank count out of sync?");
		}

		public Variable GetLastKey(Malloc heap)
		{
			if (Count == 0) return Variable.NULL;
			while (_list.Count > 0)
			{
				var first = _list.Get(_list.LastIdx);
				if (!first.IsDisposed) return new Variable(_list.LastIdx);
				_list.Pop();
				_blanks--;
			}
			throw new Exception("blank count out of sync?");
		}

		public Variable GetNextKey(Variable key, Malloc heap)
		{
			if (!key.IsInt) throw new NotImplementedException(); // not sure which sorts first
			var idx = key.AsInt();
			if (idx < _list.FirstIdx) return GetFirstKey(heap);
			if (idx > _list.LastIdx) return Variable.NULL;
			for (int i = idx + 1; i <= _list.LastIdx; i++)
			{
				var curr = _list.Get(i);
				if (curr.IsDisposed) continue;
				return new Variable(i);
			}
			return Variable.NULL;
		}
		public Variable GetPrevKey(Variable key, Malloc heap)
		{
			if (!key.IsInt) throw new NotImplementedException(); // not sure which sorts first
			var idx = key.AsInt();
			if (idx < _list.FirstIdx) return Variable.NULL;
			if (idx > _list.LastIdx) return GetLastKey(heap);
			for (int i = idx - 1; i >= 0; i--)
			{
				var curr = _list.Get(i);
				if (curr.IsDisposed) continue;
				return new Variable(i);
			}
			return Variable.NULL;
		}

		public bool HasKey(Variable key)
		{
			if (!key.IsInt) throw new NotImplementedException(); // not sure which sorts first
			var idx = key.AsInt();
			if (!_list.IsIdxInRange(idx)) return false;
			var curr = _list.Get(idx);
			return !curr.IsDisposed;
		}

		public IEnumerable<KeyValuePair<Variable, Variable>> IterateOrdered()
		{
			List<KeyValuePair<Variable, Variable>> list = new List<KeyValuePair<Variable, Variable>>();
			for (int i = _list.FirstIdx; i <= _list.LastIdx; i++)
			{
				var curr = _list.Get(i);
				if (curr.IsDisposed) continue;
				list.Add(new KeyValuePair<Variable, Variable>(new Variable(i), curr));
			}
			return list;
		}

		public IEnumerable<KeyValuePair<Variable, Variable>> IterateUnordered()
			=> IterateOrdered();

		public void Release(Malloc heap)
		{
			while (_list.Count > 0)
			{
				var curr = _list.Dequeue();
				if (!curr.IsDisposed)
					curr.Dispose(heap);
			}
			_blanks = 0;
		}

		public eAddResult TrySetChild(Variable key, Variable value, Malloc heap)
		{
			if (!key.IsInt) return eAddResult.Incompatible;
			return TrySetChild(key.AsInt(), value, heap);
		}

		public eAddResult TrySetChild(string key, Variable value, Malloc heap)
			=> eAddResult.Incompatible;

		public eAddResult TrySetChild(int index, Variable value, Malloc heap)
		{
			if (index == _list.FirstIdx - 1)
			{
				if (Count == Capacity) return eAddResult.NoSpace;
				_list.ReverseQueue(value);
				return eAddResult.Success;
			}
			if (index == _list.LastIdx + 1)
			{
				if (Count == Capacity) return eAddResult.NoSpace;
				_list.Enqueue(value);
				return eAddResult.Success;
			}
			else if (_list.IsIdxInRange(index))
			{
				var curr = _list.Get(index);
				curr.Dispose(heap);
				_list.Set(index, value);
				return eAddResult.Success;
			}
			return eAddResult.Incompatible;
		}
	}

	interface IManageReference
	{
		int GenerationID { get; set; }
		void Release(Malloc memory);
	}

	interface IHaveCapacity
	{
		int Capacity { get; }
	}
	interface IStructure : IHaveCapacity
	{
		int Count { get; }
		Variable? GetChild(Variable key);
		Variable? GetChild(string key);
		Variable? GetChild(int index);
		bool HasKey(Variable key);
		eAddResult TrySetChild(Variable key, Variable value, Malloc heap);
		eAddResult TrySetChild(string key, Variable value, Malloc heap);
		eAddResult TrySetChild(int index, Variable value, Malloc heap);
		void DeleteChild(Variable key, Malloc memory);
		Variable DeletePopChild(Variable key);

		IEnumerable<KeyValuePair<Variable, Variable>> IterateOrdered();
		IEnumerable<KeyValuePair<Variable, Variable>> IterateUnordered();
		// you better not call fill into an object that can't handle every element
		void Fill(IEnumerable<KeyValuePair<Variable, Variable>> original);
		void Dispose(); // void out contents, without adjusting checkins - used for copy
		void Release(Malloc heap); // check in all contents

		Variable GetFirstKey(Malloc heap);
		Variable GetLastKey(Malloc heap);
		Variable GetNextKey(Variable key, Malloc heap);
		Variable GetPrevKey(Variable key, Malloc heap);
	}
}
