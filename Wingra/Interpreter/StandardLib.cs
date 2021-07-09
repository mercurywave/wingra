using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wingra.Interpreter
{
	static class StandardLib
	{
		public static void Setup(ORuntime runtime)
		{

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0, 1);
				var inner = GetAssertStruct(t);
				if (inner.Count == 0)
					j.PassReturn(Variable.NULL);
				else
				{
					var key = j.TryGetPassingParam(0) ?? Variable.NULL;
					var next = inner.GetNextKey(key, j.Heap);
					j.PassReturn(next);
				}
				j.CheckIn(t.Value);
			}, "NextKey", "Obj");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0, 1);
				var inner = GetAssertStruct(t);
				if (inner.Count == 0)
					j.PassReturn(Variable.NULL);
				else
				{
					var key = j.TryGetPassingParam(0) ?? Variable.NULL;
					var next = inner.GetPrevKey(key, j.Heap);
					j.PassReturn(next);
				}
				j.CheckIn(t.Value);
			}, "PrevKey", "Obj");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var inner = GetAssertStruct(t);
				j.PassReturn(new Variable(inner.Count));
				j.CheckIn(t.Value);
			}, "Count", "Obj");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var inner = GetAssertStruct(t);
				var list = j.Heap.CheckOutList(inner.Count);
				int idx = 0;
				foreach (var pair in inner.IterateUnordered())
					list.TrySetChild(new Variable(idx++), pair.Key.DuplicateRaw(), j.Heap);
				j.PassReturn(new Variable(list, j.Heap));
				j.CheckIn(t.Value);
			}, "Keys", "Obj");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var inner = GetAssertStruct(t);
				var obj = inner.ShallowCopy(j.Heap);
				j.PassReturn(new Variable(obj, j.Heap));
				j.CheckIn(t.Value);
			}, "ShallowCopy", "Obj");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var obj = t.Value;
				if (obj.IsStructLike)
					j.PassReturn(new Variable(obj.HasChildren()));
				else
					j.PassReturn(new Variable(false));
				j.CheckIn(t.Value);
			}, "HasChildren", "Obj");




			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var inner = GetAssertStruct(t);
				var key = j.GetPassingParam(0);
				var child = inner.TryGetChild(key);
				j.PassReturn(new Variable(child.HasValue));
				j.CheckIn(t.Value);
			}, "Has", "Map");



			runtime.InjectExternalCall((j, t) =>
			{
				AddToListHelper(j, t);
			}, "Add", "List");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var inner = GetAssertStruct(t);
				var comp = j.GetPassingParam(0);
				bool found = inner.IterateUnordered().Any(p => p.Value.ContentsEqual(comp));
				j.PassReturn(new Variable(found));
				j.CheckIn(t.Value);
			}, "Contains", "List");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var inner = GetAssertStruct(t);
				var lamb = j.GetPassingParam(0);
				bool found = inner.IterateUnordered().Any(p =>
				{
					var test = j.RunLambda(lamb, "it", p.Value.DuplicateAsRef());
					if (test.HasValue) return test.Value.AsBool();
					return false;
				});
				j.PassReturn(new Variable(found));
				j.CheckIn(t.Value);
			}, "Any", "List");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var inner = GetAssertStruct(t);
				var lamb = j.GetPassingParam(0);
				var found = inner.IterateUnordered().Where(p =>
				{
					var test = j.RunLambda(lamb, "it", p.Value.DuplicateAsRef());
					if (test.HasValue) return test.Value.AsBool();
					return false;
				});
				foreach (var p in found.ToList())
					inner.DeleteChild(j.Heap, p.Key);
				j.ReturnNothing();
				j.CheckIn(t.Value);
			}, "RemoveAll", "List");




			runtime.InjectExternalCall((j, t) =>
			{
				AddToListHelper(j, t);
			}, "Push", "Stack");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var inner = GetAssertStruct(t);
				if (inner.Count == 0)
					j.PassReturn(Variable.NULL);
				else
				{
					var last = inner.GetLastKey(j.Heap);
					var obj = inner.DeletePopChild(last);
					j.PassReturn(obj);
				}
				j.CheckIn(t.Value);
			}, "Pop", "Stack");




			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var value = j.GetPassingParam(0);
				var inner = GetAssertStruct(t);
				inner.OptimizeStructure(StructPointer.eKeyType.Queue, j.Heap);
				var lastVar = inner.GetLastKey(j.Heap);
				var last = 0;
				if (lastVar.IsInt) last = lastVar.AsInt() + 1;
				inner.SetChild(last, value, j.Heap);
				j.ReturnNothing();
				j.CheckIn(t.Value);
			}, "Enqueue", "Queue");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var inner = GetAssertStruct(t);
				if (inner.Count == 0)
					j.PassReturn(Variable.NULL);
				else
				{
					var first = inner.GetFirstKey(j.Heap);
					var obj = inner.DeletePopChild(first);
					j.PassReturn(obj);
				}
				j.CheckIn(t.Value);
			}, "Dequeue", "Queue");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var inner = j.Heap.CheckOutQueue(4);
				j.PassReturn(new Variable(inner, j.Heap));
			}, "New", "Queue");



			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1, 2);
				var input = GetThisString(t);
				var toRemove = j.GetPassingParam(0).AsString();
				var replacement = j.TryGetPassingParam(1)?.AsString() ?? "";
				var output = input.Replace(toRemove, replacement);
				j.PassReturn(new Variable(output));
			}, "Replace", "Str");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1, 2);
				var input = GetThisString(t);
				var delim = j.GetPassingParam(0).AsString();
				var piece = j.TryGetPassingParam(1)?.AsInt() ?? 1;
				var output = util.Piece(input, delim, piece);
				j.PassReturn(new Variable(output));
			}, "Piece", "Str");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1, 2);
				var input = GetThisString(t);
				var start = j.GetPassingParam(0).AsInt();
				var len = j.TryGetPassingParam(1)?.AsInt() ?? 1;
				var output = util.BoundedSubstr(input, start, len);
				j.PassReturn(new Variable(output));
			}, "SubStr", "Str");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var input = GetThisString(t);
				j.PassReturn(new Variable(input.Length));
			}, "Len", "Str");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var input = GetThisString(t);
				var search = j.GetPassingParam(0).AsString();
				var output = input.Contains(search);
				j.PassReturn(new Variable(output));
			}, "Contains", "Str");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var input = GetThisString(t);
				var delim = j.GetPassingParam(0).AsString();
				var arr = util.Split(input, delim);
				var list = runtime.Heap.CheckOutList(arr.Length);
				for (int i = 0; i < arr.Length; i++)
					list.TrySetChild(i, new Variable(arr[i]), runtime.Heap);
				j.PassReturn(new Variable(list, runtime.Heap));
			}, "Split", "Str");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var input = GetThisString(t);
				j.PassReturn(new Variable(input.ToUpper()));
			}, "ToUpper", "Str");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var input = GetThisString(t);
				j.PassReturn(new Variable(input.ToLower()));
			}, "ToLower", "Str");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var input = GetThisString(t);
				j.PassReturn(new Variable(input.Trim()));
			}, "Trim", "Str");



			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var input = GetThisInt(t);
				if (!(j is AsyncJob)) throw new RuntimeException("cannot wait in synchronous job");
				var task = j.Runtime.GetBackgroundJob(input);
				if (task == null) return;
				var aj = j as AsyncJob;
				aj._task = task.WaitObserveCompletion();
				j.ReturnNothing();
			}, "Wait", "Job");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(0);
				var input = GetThisInt(t);
				var task = j.Runtime.GetBackgroundJob(input);
				j.PassReturn(new Variable(task == null));
			}, "IsComplete", "Job");

			runtime.InjectExternalAsyncCall(async (j, t) =>
			{
				await Task.Yield();
				j.ReturnNothing();
			}, "Yield", "Job");

			runtime.InjectExternalAsyncCall(async (j, t) =>
			{
				j.AssertPassingParams(1);
				var ms = j.GetPassingParam(0).AsInt();
				await Task.Delay(ms);
				j.ReturnNothing();
			}, "Pause", "Job");


			runtime.InjectDynamicLibrary(new LDebug(runtime), "Debug");
			runtime.InjectDynamicLibrary(new LScratch(runtime), "Scratch");
			runtime.InjectDynamicLibrary(new LPromise(runtime), "Promise");
			runtime.InjectDynamicLibrary(new LMath(runtime), "Math");
		}

		private static void AddToListHelper(Job j, Variable? thisVar)
		{
			j.AssertPassingParams(1);
			var inner = GetAssertStruct(thisVar);
			var obj = j.GetPassingParam(0);
			if (inner.Count > 0)
			{
				var last = inner.GetLastKey(j.Heap);
				if (!last.IsInt) throw new RuntimeException("trailing node is not an integer");
				inner.SetChild(last.AsInt() + 1, obj, j.Heap);
			}
			else
				inner.SetChild(0, obj, j.Heap);
			j.PassReturn(obj.DuplicateAsRef());
			j.CheckIn(thisVar.Value);
		}

		static StructPointer GetAssertStruct(Variable? thisVar)
		{
			if (!thisVar.HasValue) throw new RuntimeException("Function expects to be called like a method");
			var list = thisVar.Value;
			if (!list.IsStructLike) throw new RuntimeException("Passed in method scope is not an object");
			if (!list.IsPointerValid) throw new RuntimeException("invalid pointer");
			return list.GetStruct();
		}
		static string GetThisString(Variable? thisVar)
		{
			if (thisVar.HasValue) return thisVar.Value.AsString();
			return "";
		}
		static int GetThisInt(Variable? thisVar)
		{
			if (thisVar.HasValue) return thisVar.Value.AsInt();
			throw new RuntimeException("function expected int, found null");
		}
		class LDebug
		{
			ORuntime _run;
			public LDebug(ORuntime run) { _run = run; }
			public void Break()
			{
				throw new RuntimeException("breakpoint reached");
			}
			public string ObjDebug(Variable obj)
			{
				var text = obj.ToString();
				_run.CheckIn(obj);
				return text;
			}
			public string TypeName(Variable obj)
			{
				var text = obj.TypeName;
				_run.CheckIn(obj);
				return text;
			}
		}

		class LMath
		{
			ORuntime _run;
			public LMath(ORuntime run) { _run = run; }

			public Variable Mod(Variable value, Variable mod)
			{
				if (value.IsInt && mod.IsInt)
				{
					var v = value.AsInt();
					var m = mod.AsInt();
					if (v < 0) return new Variable(m - ((-v) % m));
					return new Variable(v % m);
				}
				else
				{
					var v = value.AsFloat();
					var m = mod.AsFloat();
					if (v < 0) return new Variable(m - ((-v) % m));
					return new Variable(v % m);
				}
			}

			//because -2/10 is the same as 4/10
			public Variable Div(Variable value, Variable div)
			{
				if (value.IsInt && div.IsInt)
				{
					var v = value.AsInt();
					var d = div.AsInt();
					if (v < 0) return new Variable((v - d) / d);
					return new Variable(v / d);
				}
				else
				{
					var v = value.AsFloat();
					var d = div.AsFloat();
					if (v < 0) return new Variable((v - d) / d);
					return new Variable(v / d);
				}
			}

			public int Floor(float val)
			{
				if (val < 0) return (int)(val - 1);
				return (int)val;
			}
			public int Ceiling(float val)
			{
				if (val < 0) return (int)val;
				return (int)(val + 1);
			}
			public int Round(float val)
			{
				return (int)Math.Round(val);
			}
			public float RoundToNearest(float val, float nearest)
			{
				return (float)(Math.Round(val / nearest) * nearest);
			}

			public float Sqrt(float val)
			{
				return (float)Math.Sqrt(val);
			}

			public float Atan2(float y, float x)
				=> (float)Math.Atan2(y, x);

		}

		class LScratch
		{
			ORuntime _run;
			// all the alternative ways to manage this kinda suck
			HashSet<IManageReference> _checkedOut = new HashSet<IManageReference>();
			public LScratch(ORuntime run) { _run = run; }
			public Variable Alloc()
			{
				var inner = _run.Heap.CheckOutStruct();
				inner.Reset(_run.Heap.CheckOutList(4));
				if (_checkedOut.Contains(inner))
					throw new RuntimeException("attempted to check out leaked memory");
				_checkedOut.Add(inner);
				return new Variable(inner).MakePointer(); // caller doesn't never gets the original
			}
			public void Free(Variable scratch)
			{
				if (!scratch.IsPointer || !scratch.IsPointerValid)
					throw new RuntimeException("attempted to free released memory");
				var inner = scratch.Inner;
				if (inner == null)
					throw new RuntimeException("cannot check in non-struct");
				if (!_checkedOut.Remove(inner))
					throw new RuntimeException("attempted to free non-temp object");
				scratch.Dispose(_run.Heap);
			}
			public Variable Hoist(Variable scratch)
			{
				if (scratch.IsPointer) throw new RuntimeException("cannot hoist pointer");
				if (!scratch.IsPointerValid || !scratch.OwnsHeapContent) throw new RuntimeException("invalid pointer");
				var inner = scratch.Inner;
				_checkedOut.Add(inner);
				return scratch.DuplicateAsRef();
			}
		}

		class LPromise
		{
			ORuntime _run;
			int _idx = 0;
			Dictionary<int, TaskCompletionSource<bool>> _waiters = new Dictionary<int, TaskCompletionSource<bool>>();
			public LPromise(ORuntime run)
			{
				_run = run;

				run.InjectExternalAsyncCall(async (j, t) =>
				{
					j.AssertPassingParams(0);
					var id = GetThisInt(t);
					if (!_waiters.ContainsKey(id))
						return;
					var tcs = _waiters[id];
					await tcs.Task;
					j.ReturnNothing();
				}, "Wait", "Promise");

				run.InjectExternalCall((j, t) =>
				{
					j.AssertPassingParams(0);
					var id = GetThisInt(t);
					if (!_waiters.ContainsKey(id))
						throw new RuntimeException("Promise not initialized", j);
					var tcs = _waiters[id];
					tcs.TrySetResult(true);
					_waiters.Remove(id);
					j.ReturnNothing();
				}, "Resolve", "Promise");

				run.InjectExternalCall((j, t) =>
				{
					// let all waiters advance, but don't clean up the handle
					j.AssertPassingParams(0);
					var id = GetThisInt(t);
					if (!_waiters.ContainsKey(id))
						throw new RuntimeException("Promise not initialized", j);
					_waiters[id].TrySetResult(true);
				}, "Broadcast", "Promise");
			}
			public int Create()
			{
				_idx++;
				_waiters.Add(_idx, new TaskCompletionSource<bool>());
				return _idx;
			}
		}
	}
}
