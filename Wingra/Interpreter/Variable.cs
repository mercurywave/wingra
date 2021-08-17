using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Wingra.Interpreter
{
	//TODO: evaluate performance of [StructLayout(LayoutKind.Explicit)] with [FieldOffset()]
	// also maybe try this as a class
	public struct Variable : IComparable<Variable>
	{
		enum eVar
		{
			Disposed = 0,
			Null = 1,
			Int = 2,
			Float = 3,
			String = 4,
			Struct = 5,
			Lambda = 6,
			Iterator = 7,
			Pointer = 8,
			Bool = 9,
			Enum = 10,
			ExternalObject = 11,
			IsChild = 16, // everything here and larger is a bitflag
			IsGlobal = 32,
			IsReadonly = 64,
			CanAutoConvert = 128,
		}
		const int TYPE_RESERVED = 15;

		public Variable(int num)
		{
			_float = 0; _string = null; _ref = null;
			_flag = eVar.Int; _int = num;
		}
		public Variable(bool value)
		{
			_float = 0; _string = null; _ref = null;
			_flag = eVar.Bool; _int = value ? 1 : 0;
		}
		public Variable(float num)
		{
			_string = null; _ref = null; _int = 0;
			_flag = eVar.Float; _float = num;
		}
		public Variable(string str)
		{
			_float = 0; _ref = null; _int = 0;
			_flag = eVar.String; _string = str;
		}
		internal Variable(ILambda lamb)
		{
			_float = 0; _string = null;
			_flag = eVar.Lambda; _ref = lamb;
			_int = lamb.GenerationID;
		}
		// static function pointer
		internal Variable(CodeBlock code)
		{
			_float = 0; _string = null;
			_flag = eVar.Lambda;
			var lamb = new LambdaPointer();
			lamb.Reset(code);
			_ref = lamb;
			_int = lamb.GenerationID;
		}
		internal Variable(CodeBlock code, Malloc heap)
		{
			_float = 0; _string = null;
			_flag = eVar.Lambda;
			var lamb = heap.CheckOutFuncPoint();
			lamb.Reset(code);
			_ref = lamb;
			_int = lamb.GenerationID;
		}

		internal Variable(StructPointer list)
		{
			_float = 0; _string = null;
			_flag = eVar.Struct; _ref = list;
			_int = list.GenerationID;
		}

		internal Variable(IStructure list, Malloc heap)
		{
			_float = 0; _string = null;
			_flag = eVar.Struct; _ref = heap.CheckOutStruct(list);
			_int = _ref.GenerationID;
		}
		internal Variable(IIterate list, Malloc heap)
		{
			_float = 0; _string = null;
			_flag = eVar.Iterator; _ref = heap.CheckOutIterator(list);
			_int = _ref.GenerationID;
		}

		internal Variable(string path, Variable enumValue, Malloc heap)
		{
			_float = 0; _int = 0;
			_string = path;
			_flag = eVar.Enum | eVar.IsGlobal;
			_ref = heap.CheckOutEnumPointer(enumValue);
		}
		internal Variable(ExternalWrapper extObj)
		{
			_ref = extObj;
			_int = _ref.GenerationID;
			_flag = eVar.ExternalObject;
			_float = 0; _string = null;
		}
		internal Variable(IManageReference extObj)
		{
			_ref = extObj;
			_int = _ref.GenerationID;
			_flag = eVar.Pointer;
			_float = 0; _string = null;
		}

		internal static Variable FromExternalObject(object extObj, Malloc heap)
		{
			if (extObj is IManageReference)
				return new Variable(extObj as IManageReference);
			else
			{
				var wrap = heap.CheckOutExternalWrapper();
				wrap.Internal = extObj;
				return new Variable(wrap);
			}
		}

		public static Variable NULL => new Variable() { _flag = eVar.Null };
		public static Variable DISPOSED => new Variable() { _flag = eVar.Disposed };

		eVar _flag;
		eVar _dataType => (eVar)((int)_flag & TYPE_RESERVED);
		int _int;
		float _float;
		string _string;
		IManageReference _ref;

		int _AsInt()
		{
			if (_dataType == eVar.Int) return _int;
			if (_dataType == eVar.Float) return (int)_float;
			if (_dataType == eVar.Bool) return _int;
			throw new RuntimeException("Variable is not an integer");
		}
		public int AsInt()
		{
			if (CanAutoConvert) return AsIntLoose();
			return _AsInt();
		}
		public int AsIntLoose()
		{
			if (_dataType == eVar.String) return int.Parse(_string, CultureInfo.InvariantCulture);
			if (IsEnum) return EnumContent.AsIntLoose();
			return _AsInt();
		}
		float _AsFloat()
		{
			if (_dataType == eVar.Int) return _int;
			if (_dataType == eVar.Float) return _float;
			throw new RuntimeException("Variable is not a float");
		}
		public float AsFloat()
		{
			if (CanAutoConvert) return AsFloatLoose();
			return _AsFloat();
		}
		public float AsFloatLoose()
		{
			if (_dataType == eVar.String) return float.Parse(_string, CultureInfo.InvariantCulture);
			if (IsEnum) return EnumContent.AsFloatLoose();
			return _AsFloat();
		}
		string _AsString()
		{
			if (_dataType == eVar.String) return _string;
			throw new RuntimeException("Variable is not a string");
		}
		public string AsString()
		{
			if (CanAutoConvert) return AsStringLoose();
			return _AsString();
		}
		public string AsStringLoose()
		{
			if (_dataType == eVar.Int) return "" + _int;
			if (_dataType == eVar.Float) return "" + _float;
			if (_dataType == eVar.Bool) return (_int != 0 ? "true" : "false");
			if (IsEnum) return EnumContent.GetValueString();
			return _AsString();
		}
		bool _AsBool()
		{
			if (_dataType == eVar.Bool) return _int != 0;
			throw new RuntimeException("Variable is not a bool-like");
		}
		public bool AsBool()
		{
			if (CanAutoConvert) return AsBoolLoose();
			return _AsBool();
		}
		public bool AsBoolLoose()
		{
			if (_dataType == eVar.Int) return _int != 0;
			if (_dataType == eVar.Float) return _float != 0;
			if (_dataType == eVar.String) return !string.IsNullOrEmpty(_string);
			if (_dataType == eVar.Null || _dataType == eVar.Disposed) return false;
			if (HasHeapContent) return HasValue;
			return _AsBool();
		}
		public IEnumerable<KeyValuePair<Variable, Variable>> Children()
		{
			if (!IsStructLike) return new Dictionary<Variable, Variable>();
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			return GetStruct().IterateOrdered();
		}
		public IEnumerable<Variable> ChildKeys()
		{
			if (!IsStructLike) return new List<Variable>();
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			return GetStruct().IterateOrdered().Select(p => p.Key);
		}
		public List<Variable> AsList() => ChildValues().ToList();
		public IEnumerable<Variable> ChildValues()
		{
			if (!IsStructLike) return new List<Variable>();
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			return GetStruct().IterateOrdered().Select(p => p.Value);
		}
		public Dictionary<string, Variable> AsDictionary()
		{
			if (!IsStructLike) return new Dictionary<string, Variable>();
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			var inner = GetStruct().IterateOrdered().ToList();
			var dict = new Dictionary<string, Variable>(inner.Count);
			foreach (var pair in inner)
				dict.Add(pair.Key.AsString(), pair.Value);
			return dict;
		}

		internal bool IsChild => util.IsBitSet(_flag, eVar.IsChild);
		internal bool IsGlobal => util.IsBitSet(_flag, eVar.IsGlobal);
		internal bool IsReadonly => util.IsBitSet(_flag, eVar.IsReadonly);
		internal bool CanAutoConvert => util.IsBitSet(_flag, eVar.CanAutoConvert);
		public bool HasValue => _dataType != eVar.Disposed && _dataType != eVar.Null;
		internal bool IsDisposed => _dataType == eVar.Disposed;
		internal bool IsClean => !OwnsHeapContent;
		internal bool IsNull => _dataType == eVar.Null;
		public bool IsNumeric => _dataType == eVar.Int || _dataType == eVar.Float;
		public bool IsInt => _dataType == eVar.Int;
		public bool IsFloat => _dataType == eVar.Float;
		public bool IsBool => _dataType == eVar.Bool;
		public bool IsString => _dataType == eVar.String;
		public bool IsEnum => _dataType == eVar.Enum;
		public bool IsLambdaLike => _dataType == eVar.Lambda || (_dataType == eVar.Pointer && _ref is LambdaPointer);
		public bool IsRealLambda => _dataType == eVar.Lambda;
		public bool IsStructLike => _dataType == eVar.Struct || (_dataType == eVar.Pointer && _ref is StructPointer) || (IsEnum && EnumContent.IsStructLike);
		public bool IsRealStruct => _dataType == eVar.Struct;
		public bool OwnsHeapContent => _dataType == eVar.Struct || _dataType == eVar.Lambda || _dataType == eVar.Iterator || IsExternalObject;
		public bool IsIteratorLike => _dataType == eVar.Iterator || (_dataType == eVar.Pointer && _ref is IteratorPointer);
		public bool IsRealIterator => _dataType == eVar.Iterator;
		public bool IsExternalObject => _dataType == eVar.ExternalObject;
		public bool IsPointer => _dataType == eVar.Pointer || IsEnum;
		internal bool HasHeapContent => IsStructLike || IsIteratorLike || IsLambdaLike || IsExternalObject;

		public override string ToString()
		{
			string type = "(" + _dataType.ToString() + ")";
			var value = GetValueString();
			if (value == "") return type;
			return value + " " + type;
		}

		public string TypeName => _dataType.ToString();
		public string GetValueString() => _GetValueString();
		string _GetValueString(int depth = 0)
		{
			if (_dataType == eVar.Int) return "" + _int;
			if (_dataType == eVar.Float) return "" + _float;
			if (_dataType == eVar.String) return _string;
			if (_dataType == eVar.Bool) return (_int != 0 ? "true" : "false");
			if (_dataType == eVar.Null) return "{null}";
			if (_dataType == eVar.Enum) return "{" + _string + "}";
			if (!IsPointerValid) return "!!!attempt to access already-freed object";
			if (IsLambdaLike) return (IsPointer ? "*" : "") + "{lambda}";
			if (IsExternalObject) return "ext:" + GetExternalContents().ToString();
			if (IsStructLike)
			{
				if (depth > 4) return "[...]";
				var st = GetStruct();
				if (st.Count > 4) return "(" + st.Count + ")";
				var pack = "";
				foreach (var pair in st.IterateOrdered())
					pack = util.AppendPiece(pack, ",", pair.Key._GetValueString(depth + 1) + ":" + pair.Value._GetValueString(depth + 1));
				if (pack.Length > 40) pack = pack.Substring(0, 37) + "...";
				return (IsPointer ? "*" : "") + "[" + pack + "]";
			}
			return "";
		}

		#region lambdas
		internal bool IsExecutable => IsLambdaLike;
		internal ILambda GetLambdaInternal() => _ref as ILambda;

		public string GetLambdaDebugCode() => GetLambdaInternal().GetDebugName();

		#endregion

		public object GetExternalContents()
		{
			if(_ref is ExternalWrapper)
				return (_ref as ExternalWrapper).Internal;
			return _ref;
		}


		#region enums
		internal Variable EnumContent => (_ref as EnumPointer).Contents;
		internal Variable GetEnumContentMeh()
		{
			var inner = (_ref as EnumPointer).Contents;
			inner.FlagAsMeh();
			return inner;
		}
		#endregion

		internal bool IsPointerValid
			=> !IsPointer || _int == _ref.GenerationID;

		internal Variable MakePointer()
		{
			if (!HasHeapContent || IsEnum) return this; // probably a pointer
														//Debug.Assert(_ref == null || _ref.GenerationID == _int);
			if (_ref != null && _ref.GenerationID != _int)
				throw new RuntimeException("pointer out of date!");
			return new Variable()
			{
				_flag = eVar.Pointer,
				_int = _int,
				_ref = _ref,
			};
		}
		internal Variable DuplicateRaw() => this; // works because struct // PERF: not actually needed
		internal Variable DuplicateAsRef()
		{
			if (HasHeapContent) return MakePointer();
			return this;
		}
		internal Variable DeepCopy(Malloc heap)
			=> DeepCopy(heap, true);
		internal Variable DeepCopy(Malloc heap, bool copyPointer)
		{
			if (!HasHeapContent) return DuplicateRaw();
			if (!copyPointer && IsPointer) return DuplicateRaw();
			if (IsStructLike)
			{
				var inner = heap.CheckOutDuplicate(_ref);
				if (!(inner is StructPointer)) throw new NotImplementedException();
				return new Variable(inner as StructPointer);
			}
			if (IsLambdaLike)
			{
				var inner = GetLambdaInternal();
				if (inner is LambdaPointer)
				{
					var orig = inner as LambdaPointer;
					var lamb = heap.CheckOutFuncPoint();
					lamb.CopyFrom(heap, orig);
					return new Variable(lamb);
				}
			}
			throw new NotImplementedException();
		}
		internal void NullOut()
		{
			if (HasValue) throw new Exception("nulling out a non-disposed value");
			_flag = eVar.Null;
			_int = 0;
			_float = 0;
			_string = null;
			_ref = null;
		}
		// used during copy operations to wipe out an old copy efficiently
		internal void FlagDisposed() { _flag = eVar.Disposed; }
		internal void Dispose(Malloc memory)
		{
			if (IsDisposed) return; // TODO: this shouln't be neccessary, but I think there is a variable leak
			var rel = _ref as IReleaseMemory;
			if (rel != null && OwnsHeapContent)
				rel.Release(memory);
			_flag = eVar.Disposed;
		}
		internal void FreeContents(Malloc memory)
		{
			if (IsDisposed) return; // TODO: this shouln't be neccessary, but I think there is a variable leak
			var rel = _ref as IReleaseMemory;
			if (rel != null)
				rel.Release(memory);
			_flag = eVar.Disposed;
		}

		internal void FlagAsGlobal()
		{
			util.SetBit(ref _flag, eVar.IsGlobal);
		}
		internal void FlagAsData()
		{
			//TODO: this doesn't set owned children as read-only, so they might accidentally be modifiable
			util.SetBit(ref _flag, eVar.IsGlobal);
			util.SetBit(ref _flag, eVar.IsReadonly);
		}

		internal void FlagAsMeh()
		{
			util.SetBit(ref _flag, eVar.CanAutoConvert);
		}

		#region structure helpers
		public bool HasChildren() => IsStructLike ? GetStruct().Count > 0 : false;
		public bool HasChildKey(Variable key) => TryGetChild(key).HasValue;
		public bool HasChildKey(string key) => TryGetChild(key).HasValue;

		public bool HasChildKey(int key) => TryGetChild(key).HasValue;

		internal Variable GetChild(Variable prop)
			=> TryGetChild(prop).Value;
		public Variable? TryGetChild(Variable prop)
		{
			if (prop.IsInt) return TryGetChild(prop._int);
			if (prop.IsString) return TryGetChild(prop._string);
			if (!IsStructLike) return null;
			if (IsExternalObject) throw new RuntimeException("cannot access contents of external object");
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			var array = GetStruct();
			return array.TryGetChild(prop);
		}
		public Variable? TryGetChild(string prop)
		{
			if (!IsStructLike) return null;
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			if (IsExternalObject) throw new RuntimeException("cannot access contents of external object");
			var array = GetStruct();
			return array.TryGetChild(prop);
		}
		public Variable? TryGetChild(int index)
		{
			if (!IsStructLike) return null;
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			if (IsExternalObject) throw new RuntimeException("cannot access contents of external object");
			var array = GetStruct();
			return array.TryGetChild(index);
		}

		internal void SetChild(Variable key, Variable value, Malloc heap)
		{
			if (!IsStructLike) throw new RuntimeException("cannot save to non-struct");
			if (!IsPointerValid) throw new RuntimeException("attempt to save to already-freed object");
			if (IsExternalObject) throw new RuntimeException("cannot access contents of external object");
			var array = GetStruct();
			array.SetChild(key, value, heap);
		}

		internal Variable FreePopChild(Variable key)
		{
			if (!IsStructLike) throw new RuntimeException("cannot free non-struct");
			if (!IsPointerValid) throw new RuntimeException("attempt to free already-freed object");
			if (IsExternalObject) throw new RuntimeException("cannot access contents of external object");
			var array = GetStruct();
			return array.DeletePopChild(key);
		}

		internal IManageReference Inner => _ref;
		internal StructPointer GetStruct() => IsEnum ? EnumContent.GetStruct() : _ref as StructPointer;
		public IEnumerable<KeyValuePair<Variable, Variable>> IterateChildrenUnordered()
			=> GetStruct().IterateUnordered();
		public IEnumerable<KeyValuePair<Variable, Variable>> IterateChildrenOrdered()
			=> GetStruct().IterateOrdered();

		internal Variable GetFirstKey(Malloc heap)
		{
			if (!IsStructLike) return new Variable();
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			if (IsExternalObject) throw new RuntimeException("cannot access contents of external object");
			var inner = GetStruct();
			return inner.GetFirstKey(heap);
		}
		internal Variable GetNextKey(Variable key, Malloc heap)
		{
			if (!IsStructLike) return new Variable();
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			if (IsExternalObject) throw new RuntimeException("cannot access contents of external object");
			var inner = GetStruct();
			return inner.GetNextKey(key, heap);
		}
		internal Variable GetLastKey(Malloc heap)
		{
			if (!IsStructLike) return new Variable();
			if (!IsPointerValid) throw new RuntimeException("attempt to access already-freed object");
			if (IsExternalObject) throw new RuntimeException("cannot access contents of external object");
			var inner = GetStruct();
			return inner.GetLastKey(heap);
		}
		#endregion

		#region Iterator
		internal IteratorPointer GetIteratorInternal() => _ref as IteratorPointer;
		#endregion

		#region comparisons
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = _dataType.GetHashCode();
				if (IsInt) hash = hash * 31 + _int.GetHashCode();
				else if (IsFloat) hash = hash * 31 + _float.GetHashCode();
				else if (IsString) hash = hash * 31 + _string.GetHashCode();
				else if (_ref != null) hash = hash * 31 + _ref.GetHashCode();
				return hash;
			}
		}

		public override bool Equals(object obj)
			=> ContentsEqual((Variable)obj);
		public bool ContentsEqual(Variable target)
		{
			if (IsNumeric && target.IsNumeric)
			{
				if (IsInt && target.IsInt)
					return _int == target._int;
				return AsFloat() == target.AsFloat();
			}
			if (IsString && target.IsString)
				return _string == target._string;
			if (IsBool && target.IsBool)
				return _int == target._int;
			if (!HasValue && !target.HasValue)
				return true;
			if (!HasValue || !target.HasValue)
				return false;
			if (!CanAutoConvert && target.CanAutoConvert)
			{
				if (target.IsEnum) target = target.GetEnumContentMeh();
				if (IsInt) return _int == target.AsIntLoose();
				if (IsFloat) return _float == target.AsFloatLoose();
				if (IsBool) return AsBool() == target.AsBoolLoose();
				if (IsString) return _string == target.AsStringLoose();
				if (IsEnum) return EnumContent.ContentsEqual(target);
				if (!HasValue) return !target.HasValue;
			}
			else if (CanAutoConvert && !target.CanAutoConvert)
				return target.ContentsEqual(this);
			else if (CanAutoConvert && target.CanAutoConvert)
			{
				if (target.IsEnum)
					target = target.GetEnumContentMeh();
				if (IsEnum)
					return GetEnumContentMeh().ContentsEqual(target);
				if (IsNumeric || target.IsNumeric)
					return AsFloatLoose() == target.AsFloatLoose();
				if (IsString || target.IsString)
					return AsStringLoose() == target.AsStringLoose();
				throw new RuntimeException("cannot equate ~ to ~");
			}
			if (_ref != null)
				return _ref == target._ref;
			throw new RuntimeException("cannot equate variables " + GetValueString() + " = " + target.GetValueString());
		}

		public int CompareTo(Variable other)
		{
			if (IsInt && other.IsInt)
				return _int.CompareTo(other._int);
			if (IsFloat && other.IsFloat)
				return _float.CompareTo(other._float);
			if (IsNumeric && other.IsNumeric)
				return AsFloat().CompareTo(other.AsFloat());
			if (IsString && other.IsString)
				return _string.CompareTo(other._string);
			if (!HasValue && !other.HasValue)
				return 0;
			if (IsNumeric && other.IsString)
				return -1;
			if (IsString && other.IsNumeric)
				return 1;
			if (IsEnum && other.IsEnum)
			{
				if (CanAutoConvert || other.CanAutoConvert)
					if (EnumContent.HasValue && other.EnumContent.HasValue)
						return EnumContent.CompareTo(other.EnumContent);
				return _string.CompareTo(other._string);
			}
			if (IsEnum && CanAutoConvert)
				return GetEnumContentMeh().CompareTo(other);
			if (IsEnum) // other is not enum
				return -1;
			if (other.IsEnum)
				return -other.CompareTo(this);
			if (IsPointer && other.IsPointer)
			{
				if (_ref == other._ref) return 0;
				return _ref.GetHashCode().CompareTo(other._ref.GetHashCode());
			}
			if (IsPointer)
				return 1;
			if (other.IsPointer)
				return -1;
			throw new RuntimeException("cannot compare variables (" + _dataType + ", " + other._dataType + ")");
		}
		#endregion
	}

	class VariableTable
	{
		Malloc _heap;
		Dictionary<string, Variable> _table = new Dictionary<string, Variable>();
		public VariableTable(Malloc heap) { _heap = heap; }

		public Variable Get(string name) => _table[name]; // it better exist...
		public bool Has(string name) => _table.ContainsKey(name);

		public Variable? GetVarOrNull(string name)
		{
			if (_table.ContainsKey(name))
				return _table[name];
			return null;
		}
		public Variable? GetPathOrNull(string path)
		{
			var arr = util.Split(path, ".");
			if (!_table.ContainsKey(arr[0]))
				return null;
			var node = _table[arr[0]];
			for (int i = 1; i < arr.Length; i++)
			{
				var next = arr[i];
				if (!node.HasChildren() || !node.HasChildKey(next))
					return null;
				node = node.GetChild(new Variable(next));
			}
			return node;
		}

		public Variable GetOrReserve(string name)
		{
			if (!_table.ContainsKey(name))
				_table[name] = new Variable();
			return _table[name];
		}

		public void Set(string name, Variable val)
		{
			if (_table.ContainsKey(name))
				_table[name].Dispose(_heap);
			_table[name] = val;
		}

		public void Kill(string name)
			=> Set(name, new Variable());

		public Variable KillPop(string name)
		{
			if (!_table.ContainsKey(name))
				return new Variable();
			var obj = _table[name];
			_table.Remove(name);
			return obj;
		}
		public IEnumerable<KeyValuePair<string, Variable>> IterPairs() => _table;
	}

	class VariableList : List<Variable>, IHaveCapacity
	{
		public VariableList(int capacity) : base(capacity)
		{
			for (int i = 0; i < capacity; i++)
				Add(new Variable());
		}
	}
}
