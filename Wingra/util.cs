using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wingra
{
	public static class Consts
	{
		public const string INIT_FUNC = "***init";
		public const string ITERATOR_VAR = "***iter";
	}

	static class util
	{
		//not copied fomr GameLib!
		public static string GetShortFileName(string file)
		{
			var shortName = file;
			var slash = shortName.LastIndexOfAny(new char[] { '/', '\\' });
			if (slash > 0)
				shortName = shortName.Substring(slash + 1);
			return shortName;
		}




		public static string[] Split(string str, string delim)
		{
			// by default, split returns a single "" element, which is never what I want
			if (str == "") return new string[] { };
			string[] arr = new string[] { delim };
			return str.Split(arr, StringSplitOptions.None);
		}

		public static string Join(IEnumerable<string> pieces, string delim)
		{
			return string.Join(delim, pieces);
		}

		//append another piece to end (useful for multi-line output when some lines are blank)
		public static string AppendPiece(string start, string splitter, string append)
		{
			if (start == "") return append;
			return start + splitter + append;
		}

		public static string RepeatString(string str, int count, string delimiter = "")
		{
			if (count == 0) return "";
			var output = str;
			for (int i = 0; i < count - 1; i++)
				output += delimiter + str;
			return output;
		}

		//safe and lazy sub string - so if you go over the length it returns up to the end
		public static string BoundedSubstr(string str, int start, int length)
		{
			if (start < 0) { length += start; start = 0; }
			if (start + length > str.Length) length = str.Length - start;
			if (length <= 0) return "";
			return str.Substring(start, length);
		}

		public static string AppendLine(string start, string append) { return AppendPiece(start, "\n", append); }

		public static string Piece(string str, string delim, int pc, int pcTo = -1)
		{
			int start, end;
			if (pcTo < 0) pcTo = pc;
			start = DelimPos(str, delim, pc - 1);
			end = DelimPos(str, delim, pcTo, start, pc - 1);
			if (start == end) return "";
			return str.Substring(start + 1, end - start - 1);
		}

		//start should be initialized to 0
		public static string NextPiece(string str, string delim, ref int start)
		{
			int end;
			end = str.IndexOf(delim, start + 1);
			if (end < 0) end = str.Length;
			string ret = str.Substring(start, end - start);
			start = end + 1;
			return ret;
		}

		static int DelimPos(string str, string delim, int pc, int start = -1, int startPc = 0)
		{
			if (pc <= startPc) return start;
			for (; startPc < pc && start < str.Length; startPc++)
			{
				start = str.IndexOf(delim, start + 1);
				if (start < 0) return str.Length;
			}
			return start;
		}


		//copies elements of list from one to another
		public static List<T> CopyList<T>(List<T> list)
		{
			return list.ToList();
		}
		public static void AppendList<T>(List<T> list, List<T> addition)
		{
			list.AddRange(addition.ToArray());
		}

		#region Async stuff
		// defer a task that runs at the end of update
		public static void Defer(Task t) { }
		public static void Defer(Func<Task> t) { Defer(t()); }
		#endregion

		public static bool IsBitSet<T>(T flags, T flag) where T : struct
		{
			int flagsValue = (int)(object)flags;
			int flagValue = (int)(object)flag;

			return (flagsValue & flagValue) != 0;
		}

		public static void SetBit<T>(ref T flags, T flag) where T : struct
		{
			int flagsValue = (int)(object)flags;
			int flagValue = (int)(object)flag;

			flags = (T)(object)(flagsValue | flagValue);
		}

		public static void UnsetBit<T>(ref T flags, T flag) where T : struct
		{
			int flagsValue = (int)(object)flags;
			int flagValue = (int)(object)flag;

			flags = (T)(object)(flagsValue & (~flagValue));
		}


		public static T[] RangeSubset<T>(this T[] array, int startIndex, int length)
		{
			T[] subset = new T[length];
			if (length == 0) return subset;
			Array.Copy(array, startIndex, subset, 0, length);
			return subset;
		}
		public static T[] RangeFront<T>(this T[] array, int length)
			=> RangeSubset(array, 0, length);
		public static T[] RangeRemainder<T>(this T[] array, int start)
			=> RangeSubset(array, start, array.Length - start);

		public static List<T[]> SplitArr<T>(this T[] array, Predicate<T> test)
		{
			var list = new List<T[]>();
			if (array.Length == 0) return list;
			var curr = -1;
			while (true)
			{
				curr++;
				var next = Array.FindIndex(array, curr, test);
				if (next < 0) break;
				list.Add(array.RangeSubset(curr, next - curr));
				curr = next;
			}
			list.Add(array.RangeRemainder(curr));
			return list;
		}

		public static IEnumerable<T> EnumOptions<T>()
		{
			return Enum.GetValues(typeof(T)).Cast<T>();
		}
	}




	public class Map<Key, Value>
	{
		Dictionary<Key, List<Value>> _map;
		public Map()
		{
			_map = new Dictionary<Key, List<Value>>();
		}

		public void Add(Key key)
		{
			if (!_map.ContainsKey(key)) _map.Add(key, new List<Value>());
		}
		public void Add(Key key, Value value)
		{
			if (!_map.ContainsKey(key)) _map.Add(key, new List<Value>());
			_map[key].Add(value);
		}

		public void AddIfNotPresent(Key key, Value value)
		{
			if (!_map.ContainsKey(key)) _map.Add(key, new List<Value>());
			if (!_map[key].Contains(value))
				_map[key].Add(value);
		}

		public void Kill(Key key)
		{
			_map.Remove(key);
		}

		public void Kill(Key key, Value value)
		{
			_map[key].Remove(value);
			if (_map[key].Count == 0) _map.Remove(key);
		}

		public bool Exists(Key key)
		{
			return _map.Keys.Contains(key);
		}

		public bool Exists(Key key, Value value)
		{
			if (!_map.Keys.Contains(key)) return false;
			return _map[key].Contains(value);
		}

		public IEnumerable<Key> Keys()
		{
			foreach (Key key in _map.Keys)
				yield return key;
		}

		public int KeyCount => _map.Count;
		public IEnumerable<Value> Values(Key key)
		{
			if (key != null)
				if (_map.Keys.Contains(key))
					foreach (Value val in _map[key])
						yield return val;
		}
		public IEnumerable<KeyValuePair<Key, Value>> Values()
		{
			foreach (var key in _map.Keys)
				foreach (Value val in _map[key])
					yield return new KeyValuePair<Key, Value>(key, val);
		}
	}



	public class SortedMap<Key, Value> where Key : IComparable
	{
		SortedDictionary<Key, List<Value>> _map;
		public SortedMap()
		{
			_map = new SortedDictionary<Key, List<Value>>();
		}

		public void Add(Key key, Value value)
		{
			if (!_map.ContainsKey(key)) _map.Add(key, new List<Value>());
			_map[key].Add(value);
		}

		public void Add(Key key)
		{
			if (!_map.ContainsKey(key)) _map.Add(key, new List<Value>());
		}

		public void Kill(Key key)
		{
			_map.Remove(key);
		}

		public void Kill(Key key, Value value)
		{
			_map[key].Remove(value);
			if (_map[key].Count == 0) _map.Remove(key);
		}

		public bool Exists(Key key)
		{
			return _map.Keys.Contains(key);
		}

		public bool Exists(Key key, Value value)
		{
			if (!_map.Keys.Contains(key)) return false;
			return _map[key].Contains(value);
		}

		public IEnumerable<Key> Keys()
		{
			foreach (Key key in _map.Keys)
				yield return key;
		}

		public IEnumerable<Value> Values(Key key)
		{
			if (key != null)
				if (_map.Keys.Contains(key))
					foreach (Value val in _map[key])
						yield return val;
		}
		public int KeyCount => _map.Count;
	}



	internal class MapSet<T, U>
	{
		Dictionary<T, HashSet<U>> _dic = new Dictionary<T, HashSet<U>>();
		public void Set(T key, U value)
		{
			if (!_dic.ContainsKey(key))
				_dic.Add(key, new HashSet<U>());
			_dic[key].Add(value);
		}

		public bool Contains(T key) => _dic.ContainsKey(key);
		public bool Contains(T key, U value)
		{
			if (!Contains(key)) return false;
			return _dic[key].Contains(value);
		}

		public void Kill(T key) => _dic.Remove(key);
		public void Kill(T key, U value)
		{
			_dic[key].Remove(value);
			if (!_dic[key].Any()) Kill(key);
		}

		public IEnumerable<T> Keys() => _dic.Keys;
		public IEnumerable<U> Values(T key)
		{
			if (!Contains(key)) return new U[] { };
			return _dic[key];
		}

		public bool IsEmpty => _dic.Any();
	}

	internal class DualIndex<T, U>
	{
		Dictionary<T, HashSet<U>> _dic = new Dictionary<T, HashSet<U>>();
		Dictionary<U, HashSet<T>> _rev = new Dictionary<U, HashSet<T>>();
		public void Set(T key, U value)
		{
			if (!_dic.ContainsKey(key))
				_dic.Add(key, new HashSet<U>());
			_dic[key].Add(value);

			if (!_rev.ContainsKey(value))
				_rev.Add(value, new HashSet<T>());
			_rev[value].Add(key);
		}

		public bool ContainsValue(U value) => _rev.ContainsKey(value);
		public bool Contains(T key) => _dic.ContainsKey(key);
		public bool Contains(T key, U value)
		{
			if (!Contains(key)) return false;
			return _dic[key].Contains(value);
		}

		public void Kill(T key)
		{
			foreach (var v in Values(key))
				KillReverse(key, v);
			_dic.Remove(key);
		}
		public void Kill(T key, U value)
		{
			_dic[key].Remove(value);
			if (!_dic[key].Any()) Kill(key);
			KillReverse(key, value);
		}
		public void KillValues(U value)
		{
			foreach (var k in Keys(value).ToArray())
				Kill(k, value);
		}

		void KillReverse(T key, U value)
		{
			_rev[value].Remove(key);
			if (!_rev[value].Any()) _rev.Remove(value);
		}

		public IEnumerable<T> Keys() => _dic.Keys;
		public IEnumerable<T> Keys(U value)
		{
			if (!ContainsValue(value)) return new T[] { };
			return _rev[value];
		}
		public IEnumerable<U> Values() => _rev.Keys;
		public IEnumerable<U> Values(T key)
		{
			if (!Contains(key)) return new U[] { };
			return _dic[key];
		}

		public DualIndex<T, U> Duplicate()
		{
			return new DualIndex<T, U>()
			{
				_dic = new Dictionary<T, HashSet<U>>(_dic),
				_rev = new Dictionary<U, HashSet<T>>(_rev),
			};
		}
	}



	internal class DualKeyDictionary<T, U, Value>
	{
		Dictionary<T, Dictionary<U, Value>> _dic = new Dictionary<T, Dictionary<U, Value>>();

		public void Set(T a, U b, Value val)
		{
			if (!_dic.ContainsKey(a))
				_dic.Add(a, new Dictionary<U, Value>());
			if (_dic[a].ContainsKey(b))
				_dic[a][b] = val;
			else
				_dic[a].Add(b, val);
		}

		public Value Get(T a, U b)
		{
			return _dic[a][b];
		}

		public bool HasKeys(T a, U b)
		{
			if (!_dic.ContainsKey(a)) return false;
			if (_dic[a].ContainsKey(b)) return true;
			return false;
		}

		public IEnumerable<T> PrimaryKeys()
		{
			foreach (T key in _dic.Keys)
				yield return key;
		}
		public IEnumerable<U> SecondaryKeys(T primary)
		{
			foreach (U key in _dic[primary].Keys)
				yield return key;
		}
		public IEnumerable<Value> Values(T primary)
		{
			foreach (U second in _dic[primary].Keys)
				yield return Get(primary, second);
		}
		public IEnumerable<Value> AllValues()
		{
			foreach (T a in PrimaryKeys())
				foreach (U b in SecondaryKeys(a))
					yield return Get(a, b);
		}
		public bool IsEmpty => _dic.Count > 0;
	}

	class ODisposable : IDisposable
	{
		Action _clean;
		public ODisposable(Action cleanup)
		{
			_clean = cleanup;
		}
		public ODisposable(Action startup, Action cleanup)
		{
			startup();
			_clean = cleanup;
		}

		public void Dispose()
		{
			_clean();
		}
	}

	class FastCappedQueue<T>
	{
		T[] _list;
		int _count;
		int _lowestIdx; // array index start
		int _offsetIndex; // public facing start
		public int Capacity => _list.Length;
		public int Count => _count;
		public int FirstIdx => _offsetIndex;
		public int LastIdx => _offsetIndex + _count - 1;

		public FastCappedQueue(int cap)
		{
			_list = new T[cap];
		}

		public void ResetFromIndex(int idx, int count = 0)
		{
			_count = count;
			_offsetIndex = idx;
			_lowestIdx = 0;
		}

		public int Enqueue(T value)
		{
			_list[GetRelativeIndex(_offsetIndex + _count)] = value;
			_count++;
			return _offsetIndex + _count - 1;
		}

		public int ReverseQueue(T value)
		{
			_count++;
			_lowestIdx = (_lowestIdx - 1 + Capacity) % Capacity;
			_offsetIndex--;
			_list[_lowestIdx] = value;
			return _offsetIndex;
		}

		public T Dequeue()
		{
			var idx = _lowestIdx;
			_count--;
			_offsetIndex++;
			_lowestIdx = (_lowestIdx + 1) % Capacity;
			return _list[idx];
		}

		public void Set(int idx, T value)
		{
			var real = GetRelativeIndex(idx);
			_list[real] = value;
		}

		public T Get(int idx)
			=> _list[GetRelativeIndex(idx)];


		public bool IsIdxInRange(int idx)
			=> _count > 0 && idx >= _lowestIdx && idx <= LastIdx;

		public T Pop()
		{
			_count--;
			var idx = GetRelativeIndex(_offsetIndex + _count);
			return _list[idx];
		}

		// assumes in range!
		int GetRelativeIndex(int idx)
			=> ((idx - _offsetIndex) + _lowestIdx) % Capacity;
	}
}
