using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Wingra.Interpreter
{
	public class Job
	{
		internal static MemoryBucketer<Scope> _bucketScope = new MemoryBucketer<Scope>(c => new Scope(c));

		internal FastStack<Scope> _stack = new FastStack<Scope>(20);
		internal FastStack<Scope> CallStack => _stack;
		protected Scope _current => _stack.Peek();
		public Scope CurrentScope => _stack.Depth > 0 ? _current : null;
		internal CodeBlock Code => _current._source;
		FastStack<Variable> _registers => _current._registerStack;
		internal FastStack<Variable> Registers => _registers;
		ORuntime _runtime;
		internal ORuntime Runtime => _runtime;
		internal Malloc Heap => _runtime.Heap;

		internal Job(ORuntime runtime)
		{
			_runtime = runtime;
		}
		internal Job(ORuntime runtime, Scope scp) : this(runtime)
		{
			_stack.Push(scp);
		}
		internal Job(ORuntime runtime, CodeBlock entryPoint) : this(runtime, _bucketScope.CheckOut(entryPoint._localVarIndex.Count))
		{
			_stack.Peek().Reset(entryPoint);
		}
		~Job()
		{
			while (!_stack.IsEmpty)
			{
				var scp = _stack.Pop();
				scp.Destroy(Heap, null);

				// YUCK: was running into errors when a different bug would cause
				// this to be checked in prior to completely being initialized
				if (scp.WasAllocated && !Runtime.Debug)
					_bucketScope.CheckIn(scp);
			}
		}

		// should be called each time if being reused
		public void Initialize(CodeBlock entryPoint)
		{
			while (!_stack.IsEmpty)
				UnwindStack();
			BeginExecute(entryPoint);
		}

		protected void HandleError(Exception ex)
		{
			var trap = ex as CatchableError;
			if (trap != null)
			{
				ThrowObject(trap.Contents);
				return;
			}
			var rex = ex as RuntimeException;
			if (rex != null)
			{
				rex.Owner = this;
				_runtime.RaiseError(rex);
				return;
			}
			var msg = "!!! " + ex.ToString() + "\n";
			msg = msg.Replace(@"C:\Users\mettu\source\repos\GEL", "");
			_runtime.RaiseError(new RuntimeException(msg, this));
		}

		public void RunToCompletion()
		{
			while (!_stack.IsEmpty && CurrentScope._nextLinePointer < Code.Instructions.Count)
			{
				var line = CurrentScope.AdvanceLinePointer();
				var act = Code.Instructions[line];
				try { act(this); } // TODO: the error trap should probably be moved outside the loop
				catch (Exception ex) { HandleError(ex); break; }
			}
		}

		public enum eRunStatus { Complete, Halted }
		public eRunStatus RunBoxed(int instructions = 1000)
		{
			int count = 0;
			try
			{
				while (!_stack.IsEmpty && CurrentScope._nextLinePointer < Code.Instructions.Count)
				{
					if (count > instructions) return eRunStatus.Halted;
					count++;
					var line = CurrentScope.AdvanceLinePointer();
					var act = Code.Instructions[line];
					act(this);
				}
			}
			catch (Exception ex) { HandleError(ex); }
			return eRunStatus.Complete;
		}

		// code not exepcted to return - that will pop the last stack level and error
		public Variable? RunExpression()
		{
			try
			{
				while (CurrentScope._nextLinePointer < Code.Instructions.Count)
				{
					var line = CurrentScope.AdvanceLinePointer();
					var act = Code.Instructions[line];
					act(this);
				}
			}
			catch (Exception ex) { HandleError(ex); }
			if (Registers.IsEmpty) return null;
			return Registers.Peek();
		}

		//public Variable TraceRunExpression(out Trace trace)
		//{
		//	trace = new Trace();
		//	try
		//	{
		//		while (CurrentScope._nextLinePointer < Code.Instructions.Count)
		//		{
		//			var line = CurrentScope.AdvanceLinePointer();
		//			var code = Code;
		//			var act = Code.Instructions[line];
		//			long start = Stopwatch.GetTimestamp();
		//			act(this);
		//			long end = Stopwatch.GetTimestamp();
		//			trace.SyncStack(CallStack, code, line, end - start);
		//		}
		//	}
		//	catch (Exception ex) { HandleError(ex); }
		//	trace.FlushStack();
		//	if (Registers.IsEmpty) return null;
		//	return Registers.Peek();
		//}

		// special case, primarily for macros
		internal Variable RunGetReturn()
		{
			//var topOtheStack = new Scope();
			//topOtheStack.Reset(null);
			try
			{
				//_stack.PostPend(topOtheStack);
				//while (CurrentScope != topOtheStack)
				while (!_stack.IsEmpty)
				{
					var line = CurrentScope.AdvanceLinePointer();
					var act = Code.Instructions[line];
					act(this);
				}
			}
			catch (Exception ex) { HandleError(ex); }
			return _paramStack[0]; //leaked!
		}

		public async Task RunAsyncBoxed()
		{
			int i = 1;
			try
			{
				while (!_stack.IsEmpty && CurrentScope._nextLinePointer < Code.Instructions.Count)
				{
					var line = CurrentScope.AdvanceLinePointer();
					var act = Code.Instructions[line];
					act(this);
					if (i++ % 10000 == 0) await Task.Yield();
				}
			}
			catch (Exception ex) { HandleError(ex); }
		}


		#region instruction callbacks
		internal void JumpShort(int localIdx) => CurrentScope._nextLinePointer = localIdx;

		//internal void ContinueEnumerable(OCodeIterator iterator)
		//{
		//	var topOtheStack = CallStack.Depth;
		//	var targetLine = CallStack.Peek(0)._nextLinePointer;
		//	_paramStack.Clear();
		//	CallStack.Push(iterator.Continuation);
		//	while (CallStack.Depth > topOtheStack)
		//	{
		//		var line = CurrentScope.AdvanceLinePointer();
		//		var act = Code.Instructions[line];
		//		act(this);
		//	}
		//	if (CallStack.Depth < topOtheStack - 1 || CurrentScope._nextLinePointer != targetLine)
		//		return; // we hit an error trap instead of a normal exit
		//	if (_paramStack.Count == 1)
		//	{
		//		iterator.Index++;
		//		iterator.Current = _paramStack[0];
		//	}
		//	else if (_paramStack.Count > 1)
		//	{
		//		iterator.Index++;
		//		iterator.Current = KMultiReturn.Instantiate(_paramStack);
		//	}
		//	else
		//		iterator.Index = -1;
		//	_paramStack.Clear();
		//}

		internal Scope BeginExecute(CodeBlock source)
		{
			var target = _bucketScope.CheckOut(source._localVarIndex.Count);
			target.Reset(source);
			_stack.Push(target);
			return target;
		}

		internal void FreeScopeMemory(List<int> except)
		{
			_current.Destroy(Heap, except);
		}
		internal void UnwindStack(bool free = true)
		{
			var scp = CallStack.Pop();
			if (free)
				scp.Destroy(Heap, null);
		}

		public Variable FindVarOrThrow(string name)
		{
			var val = FindVar(name);
			if (val == null) throw new RuntimeException(name + " not found");
			return val.Value;
		}
		public Variable? FindVar(string name)
		{
			if (Code._localVarIndex.ContainsKey(name))
				return CurrentScope._allocatedLocals[Code._localVarIndex[name]];
			return FindNonAllocatedVar(name);
		}
		public Variable? FindNonAllocatedVar(string name)
		{
			//if (_runtime.GlobalScope.Has(name)) // TODO: remove?
			//return _runtime.GlobalScope.Get(name);
			var val = _FinalFindVariable(name); // TODO: remove?
			if (val != null) return val.Value;
			return null;
		}
		protected virtual Variable? _FinalFindVariable(string name) => null;

		public void InjectLocal(string name, Variable value)
			=> CurrentScope.TrySaveVariable(name, value, Heap);
		public void SaveNewLocal(int idx, Variable value)
			=> CurrentScope.SaveNewLocal(idx, value);

		public void SaveNewLocal(string name, Variable value)
			=> CurrentScope.SaveNewLocal(name, value);


		public void UpdateLocal(int idx, Variable value)
			=> CurrentScope.UpdateLocal(idx, value, Heap);
		public void UpdateLocal(string name, Variable value)
		{
			CurrentScope.UpdateLocal(name, value, Heap);
		}
		#endregion


		#region stack communication
		// this is fragile
		// assumes communication between two stack levels sets up, and then processes before modifying
		// PERF: MAYBE: it might be nice to take advantage of the register stack ala dredge/mill instead of relying on a list copy
		//				this is maybe slightly faster than the old method, but still relies on copying when it doesn't _need_ to
		//				the problem is dredging from the next state is maybe offset if the source pops an OObject to run a method
		//				Also would break during a new operation, as new object gets inserted into stack
		CommStack _paramStack = new CommStack();


		public int TotalParamsPassing => _paramStack.Count;
		public Variable GetPassingParam(int idx) => _paramStack[idx];
		public Variable? TryGetPassingParam(int idx)
		{
			if (idx >= TotalParamsPassing)
				return null;
			return _paramStack[idx];
		}
		public List<Variable> GetMultiParams(int count) => _paramStack.GetRange(0, count);
		public void AssertPassingParams(int count)
		{
			if (_paramStack.Count != count)
				throw new RuntimeException("parameter count mismatch");
		}
		public void AssertPassingParams(int min, int max) // min <= params <= max
		{
			if (_paramStack.Count < min || _paramStack.Count > max)
				throw new RuntimeException("parameter count mismatch");
		}
		public bool IsGoingToPassParamIdx(int idx) => idx < _paramStack.Count;

		// pass nulls to skip specific slots
		public void AssertPassingParams(params Predicate<Variable>[] defs)
		{
			AssertPassingParams(defs.Length);
			for (int i = 0; i < defs.Length; i++)
			{
				if (defs[i] == null) continue;
				var par = GetPassingParam(i);
				if (!defs[i].Invoke(par))
					throw new RuntimeException("parameter type mismatch: not expecting " + par.TypeName + " as parameter " + (i + 1));
			}
		}


		// prep for a jump to a new scope level
		public void PassParams(int count)
			=> _paramStack.QueueFromRegister(Registers, count);

		public bool HasMoreParameters()
			=> _paramStack.Remaining() > 0;
		public Variable ReadNextParameter()
			=> _paramStack.DequeueNext();

		public Variable ReadRemainingParameters()
			=> _paramStack.DequeueRemaining(Heap);

		public void ClearParameters()
			=> _paramStack.Empty();
		public void ReadReturn()
		{
			var list = _paramStack.DequeueRemainingList();
			if (list.Count == 0)
			{
				// this isn't a great code path, but it would be a pain to handle this
				// this register is just wasted, but the code doesn't know that
				// all expressions would need to know and handle the scenario correctly
				// the best non-invasive fix is probably to handle this with an ASM optimization
				Registers.Push(new Variable());
				return;
			}
			for (int i = 1; i < list.Count; i++)
				list[i].Dispose(Heap);
			Registers.Push(list[0]);
		}
		public void ReadReturn(int count)
		{
			var popped = _paramStack.DequeueRemainingList();
			if (popped.Count < count)
				throw new RuntimeException("function did not return as many parameters as expected");

			// registers end up reversed
			for (int i = 0; i < count; i++)
				Registers.Push(popped[popped.Count - i - 1]);
			for (int i = count; i < popped.Count; i++)
				popped[popped.Count - i - 1].Dispose(Heap);
		}

		public void PassReturn(Variable var)
			=> _paramStack.Queue(var);

		public void PassReturn(params Variable[] vars)
			=> _paramStack.Queue(vars);

		internal void PassReturn(int count)
		{
			foreach (var peek in Registers.MultiPeek(count)) // PERF: maybe hide in a debug build?
				if (!peek.IsPointerValid)
					throw new RuntimeException("attempting to return pointer to freed memory");
			_paramStack.QueueFromRegister(Registers, count);
		}

		internal void ReturnNothing() => _paramStack.Empty();


		// raise an error to the current script error trap
		public void ThrowObject(Variable? obj = null)
		{
			//var err = KRuntimeError.Instantiate(obj);
			int found = -1;
			for (int i = 0; i < CallStack.Depth; i++)
			{
				if (CallStack.Peek(i)._errorTrapJump >= 0)
				{
					found = i;
					break;
				}
			}
			if (found < 0)
			{
				if (obj.HasValue)
					throw new RuntimeException("uncaught throw - " + obj.Value.GetValueString());
				throw new RuntimeException("uncaught throw");
			}
			for (int i = 0; i < found; i++)
				UnwindStack();
			CurrentScope._nextLinePointer = CurrentScope._errorTrapJump;
			CurrentScope._errorTrapJump = -1;
			if (obj.HasValue)
				CurrentScope.SaveNewLocal("error", obj.Value);
		}

		public void ThrowFatalError() => throw new RuntimeException("Fatal error");

		//just check in a bunch of variables at once
		internal void CheckIn(params Variable[] vars)
		{
			foreach (var v in vars) v.Dispose(Heap);
		}

		// make a lambda using the current scope
		public Variable MakeLambda(CodeBlock code)
		{
			var lamb = new Variable(code, Heap);
			var closure = lamb.GetLambdaInternal() as LambdaPointer;
			foreach (var ident in code._assumedVariables)
			{
				var curr = CurrentScope.TryFindVar(ident);
				var idx = code._localVarIndex[ident];
				if (curr.HasValue)
					closure.SaveToClosure(Heap, idx, curr.Value);
			}
			return lamb;
		}
		// helpers for C# integrated functions that are passed lambdas as parameters
		public Variable? RunLambda(Variable lambda) => _RunLambda(lambda);
		public Variable? RunLambda(Variable lambda, string name1, Variable value1)
			=> _RunLambda(lambda, name1, value1);
		public Variable? RunLambda(Variable lambda, string name1, Variable value1, string name2, Variable value2)
			=> _RunLambda(lambda, name1, value1, name2, value2);

		Variable? _RunLambda(Variable lambda, string name1 = "", Variable? value1 = null, string name2 = "", Variable? value2 = null)
		{
			if (!lambda.IsExecutable) throw new RuntimeException("expected lambda", this);
			var topOtheStack = CallStack.Depth;
			var targetLine = topOtheStack == 0 ? 0 : CallStack.Peek(0)._nextLinePointer;
			_paramStack.Clear();
			var exec = lambda.GetLambdaInternal();
			exec.BeginExecute(this);
			if (name1 != "") CurrentScope.TrySaveVariable(name1, value1.Value, Heap);
			if (name2 != "") CurrentScope.TrySaveVariable(name2, value2.Value, Heap); try
			{
				while (CallStack.Depth > topOtheStack)
				{
					var line = CurrentScope.AdvanceLinePointer();
					var act = Code.Instructions[line];
					act(this);
				}
			}
			catch (Exception ex) { HandleError(ex); }
			if (CallStack.Depth < topOtheStack - 1 || (topOtheStack > 0 && CurrentScope._nextLinePointer != targetLine))
				return null; // we hit an error trap instead of a normal exit
			if (_paramStack.Count == 0) return null;
			return _paramStack[0];
		}

		#endregion

		#region public helpers
		public Variable MakeVariable(int val) => new Variable(val);
		public Variable MakeVariable(float val) => new Variable(val);
		public Variable MakeVariable(string str) => new Variable(str);
		public Variable MakeVariable(List<Variable> list)
		{
			var dup = Heap.CheckOutList(list.Count);
			for (int i = 0; i < list.Count; i++)
				dup.TrySetChild(i, list[i].DuplicateRaw(), Heap);
			return new Variable(dup, Heap);
		}
		public Variable MakeVariable(Dictionary<string, Variable> dict)
		{
			var dup = Heap.CheckOutMixedStruct(dict.Count);
			foreach (var pair in dict)
				dup.TrySetChild(pair.Key, pair.Value.DuplicateRaw(), Heap);
			return new Variable(dup, Heap);
		}
		#endregion

		#region debug
		public List<Scope> GetDebugStack() => CallStack.ToList();
		#endregion
	}

	internal class AsyncJob : Job
	{
		internal Task _task = null;
		internal int ID;
		Task _jobTask = null;
		internal AsyncJob(ORuntime runtime) : base(runtime) { }

		public void Initialize(CodeBlock entryPoint, int id)
		{
			Initialize(entryPoint);
			this.ID = id;
			_jobTask = null;
		}

		internal void KickOff(VariableList capture)
		{
			if (capture != null)
				for (int i = 0; i < capture.Count; i++)
				{
					var value = capture[i];
					if (value.HasValue)
						CurrentScope.SaveNewLocal(i, value);
				}
			_jobTask = RunAsync();
		}
		private async Task RunAsync()
		{
			await Task.Yield(); // If I use arun, I probably want to fork immediately
			try
			{
				while (!_stack.IsEmpty && CurrentScope._nextLinePointer < Code.Instructions.Count)
				{
					var line = CurrentScope.AdvanceLinePointer();
					var act = Code.Instructions[line];
					try
					{
						act(this);
						if (_task != null && !_task.IsCompleted && !_task.IsCanceled)
						{
							await _task;
							_task = null;
							if (Runtime.ShuttingDown)
								return;
						}
					}
					catch (Exception ex) { HandleError(ex);  break; }
				}
			}
			catch (Exception ex) { HandleError(ex); }
			Runtime.CheckIn(this);
			_jobTask = null;
		}
		internal async Task RunToCompletionAsync()
		{
			if (_jobTask != null)
				await _jobTask;
		}

		internal async Task WaitObserveCompletion()
		{
			if (_jobTask == null) return;
			if (_jobTask.IsCompleted || _jobTask.IsCanceled) return;
			await _jobTask;
		}
	}
}
