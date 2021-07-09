using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Wingra.Interpreter
{
	class LambdaPointer : ILambda
	{
		internal CodeBlock _code;
		internal VariableList _closure = null;

		public void Reset(CodeBlock code) { _code = code; _closure = null; }

		public int GenerationID { get; set; }

		public void Release(Malloc memory)
		{
			GenerationID++;
			if (_closure != null)
			{
				//for (int i = 0; i < _closure.Count; i++)
				//	_closure[i].CheckIn(memory);
				memory.CheckIn(_closure);
				_closure = null;
			}
			memory.CheckIn(this);
		}

		public Scope BeginExecute(Job j, Variable? thisVar = null)
		{
			var scp = j.BeginExecute(_code);
			if (_closure != null)
				for (int i = 0; i < _code._localVarIndex.Count; i++)
					scp.SaveNewLocal(i, _closure[i].DuplicateAsRef());
			if (thisVar.HasValue && _code._localVarIndex.ContainsKey(THIS))
				scp.TrySaveVariable(THIS, thisVar.Value, j.Heap);
			return scp;
		}
		public const string THIS = "this";

		public void SaveToClosure(Malloc memory, int idx, Variable source)
		{
			if (_closure == null)
				_closure = memory.CheckOutVTable(_code._localVarIndex.Count);
			_closure[idx] = source.DuplicateAsRef();
		}

		public void CopyFrom(Malloc memory, LambdaPointer orig)
		{
			_code = orig._code;
			if (orig._closure != null)
				for (int i = 0; i < _code._localVarIndex.Count; i++)
					if (orig._closure[i].HasValue)
						SaveToClosure(memory, i, orig._closure[i].DeepCopy(memory, false));
		}
		public string GetDebugName()
		{
			return _code.ToString();
		}
	}

	class ExternalFuncPointer : ILambda
	{
		Action<Job, Variable?> _act;
		public ExternalFuncPointer(Action<Job, Variable?> act) { _act = act; }
		public Scope BeginExecute(Job j, Variable? thisVar = null)
		{
			_act(j, thisVar);
			return null;
		}

		public int GenerationID { get => 0; set { } }

		public void Release(Malloc memory)
		{
		}
		public string GetDebugName()
		{
			return "external call " + _act.Method.Name;
		}
	}

	class ExternalAsyncFuncPointer : ILambda
	{
		Func<Job, Variable?, Task> _act;
		public ExternalAsyncFuncPointer(Func<Job, Variable?, Task> act) { _act = act; }
		public Scope BeginExecute(Job j, Variable? thisVar = null)
		{
			var aj = j as AsyncJob;
			if (aj == null) throw new RuntimeException("cannot call async function directly - use arun or await");
			aj._task = _act(j, thisVar);
			return null;
		}

		public int GenerationID { get => 0; set { } }

		public void Release(Malloc memory)
		{
		}
		public string GetDebugName()
		{
			return "external call " + _act.Method.Name;
		}
	}

	interface ILambda : IManageReference
	{
		// returns null from external calls!
		Scope BeginExecute(Job j, Variable? thisVar = null);

		string GetDebugName();
	}
}
