using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Interpreter
{
	public class Scope : IHaveCapacity
	{
		internal Variable[] _allocatedLocals;
		//internal VariableTable _dynamicLocals = new VariableTable(); // we can't predict variables injected with $() or externally
		internal FastStack<Variable> _registerStack = new FastStack<Variable>();
		internal CodeBlock _source;
		public int Capacity => _allocatedLocals.Length;
		public bool WasAllocated => _allocatedLocals != null;

		int _currentLinePointer = -1;
		internal int _nextLinePointer = -1;

		public CodeBlock Source => _source;
		public int CurrentLinePointer => _currentLinePointer;


		public int AdvanceLinePointer()
		{
			if (_nextLinePointer != CurrentLinePointer)
				_currentLinePointer = _nextLinePointer;
			_nextLinePointer = CurrentLinePointer + 1;
			return CurrentLinePointer;
		}

		// luckily, there can only be one trap at a time for the moment
		internal int _errorTrapJump = -1;

		internal Scope() { }
		internal Scope(int capacity)
		{
			_allocatedLocals = new Variable[capacity];
		}

		public void Reset(CodeBlock source)
		{
			_currentLinePointer = 0;
			_nextLinePointer = 0;
			_errorTrapJump = -1;
			_source = source;
			if (_source != null)
				for (int i = 0; i < _source._localVarIndex.Count; i++)
					_allocatedLocals[i].NullOut();
		}

		internal void Destroy(Malloc memory, List<int> exceptLocals)
		{
			if (_allocatedLocals != null) // why did this happen? not sure I really want to dig into this
				for (int i = 0; i < _allocatedLocals.Length; i++)
				{
					if (exceptLocals != null && exceptLocals.Contains(i)) continue; // PERF: ewww
					_allocatedLocals[i].Dispose(memory);
				}
		}

		internal void InjectFromScope(List<string> names, Scope source, Malloc memory)
		{
			foreach (var nam in names)
			{
				var found = source.TryFindVar(nam);
				if (found != null)
					TrySaveVariable(nam, found.Value.MakePointer(), memory);
			}
		}
		internal void TrySaveVariable(string name, Variable value, Malloc memory)
		{
			var idx = TryGetLocalIndex(name);
			if (idx < 0) return; // not present, code doesn't care
			UpdateLocal(idx, value, memory);
		}
		internal void SaveNewLocal(int idx, Variable value)
			=> _allocatedLocals[idx] = value;

		internal void SaveNewLocal(string name, Variable value)
			=> SaveNewLocal(GetLocalIndex(name), value);


		internal void ReserveLocal(string name)
		{
			SaveNewLocal(name, new Variable());
		}

		// returns -1 if not scoped
		int TryGetLocalIndex(string name)
		{
			if (!_source._localVarIndex.ContainsKey(name))
				return -1;
			return _source._localVarIndex[name];
		}
		int GetLocalIndex(string name)
		{
			if (!_source._localVarIndex.ContainsKey(name))
				throw new Exception("variable not reserved?");
			return _source._localVarIndex[name];
		}

		internal void UpdateLocal(int idx, Variable value, Malloc memory)
		{
			if (!value.IsPointerValid)
				throw new RuntimeException("tried to save invalid pointer");
			if (_allocatedLocals[idx].HasValue)
				_allocatedLocals[idx].Dispose(memory);
			_allocatedLocals[idx] = value;
		}

		internal void UpdateLocal(string name, Variable value, Malloc memory)
			=> UpdateLocal(GetLocalIndex(name), value, memory);

		internal Variable GetLocalByIndex(int idx)
			=> _allocatedLocals[idx];

		internal Variable FindVar(string name)
		{
			if (_source._localVarIndex.ContainsKey(name))
				return _allocatedLocals[_source._localVarIndex[name]];
			throw new Exception("unknown variable " + name);
		}
		internal Variable? TryFindVar(string name)
		{
			if (_source._localVarIndex.ContainsKey(name))
				return _allocatedLocals[_source._localVarIndex[name]];
			return null;
		}
		internal Variable FreePopVar(string name)
		{
			if (!_source._localVarIndex.ContainsKey(name))
				throw new Exception("unknown variable " + name);
			var idx = _source._localVarIndex[name];
			var obj = _allocatedLocals[idx];
			_allocatedLocals[idx] = new Variable();
			return obj;
		}

		public IEnumerable<KeyValuePair<string, Variable>> DebugVariables()
		{
			if (_allocatedLocals != null)
				for (int i = 0; i < _allocatedLocals.Length; i++)
				{
					if (_allocatedLocals[i].HasValue)
					{
						string name = "???";
						foreach (var n in _source._localVarIndex)
							if (n.Value == i)
								name = n.Key;
						yield return new KeyValuePair<string, Variable>(name, _allocatedLocals[i]);
					}
				}
			//foreach (var pair in _dynamicLocals)
			//	yield return pair;
		}
	}
}
