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
				var result = (child.HasValue && child.Value.OwnsHeapContent);
				j.PassReturn(new Variable(result));
				j.CheckIn(t.Value);
			}, "Owns", "Obj");




			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var inner = GetAssertStruct(t);
				var key = j.GetPassingParam(0);
				var child = inner.TryGetChild(key);
				j.PassReturn(new Variable(child.HasValue));
				j.CheckIn(t.Value);
			}, "Has", "Set");



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


			// throwing inside a Method.Invoke creates some garbage and causes the IDE to treat it like an unhandled exception
			// so these are rolled manually to avoid the overhead, since these are more likely sources of exceptions
			void RegTypeCheck(string name, Func<Job, Variable, bool> test, string error)
			{
				runtime.InjectExternalTypeDef((j, t) =>
				{
					if (!test(j, t.Value))
						j.ThrowObject(new Variable(error));
					j.CheckIn(t.Value);
				}, name, "Type");
			}
			RegTypeCheck("Num", (j, t) => t.IsNumeric, "Not a number");
			RegTypeCheck("Int", (j, t) => t.IsInt, "Not an integer");
			RegTypeCheck("Str", (j, t) => t.IsString, "Not a string");
			RegTypeCheck("Bool", (j, t) => t.IsBool, "Not a boolean");
			RegTypeCheck("Obj", (j, t) => t.IsStructLike, "Not an object");
			RegTypeCheck("Lamnbda", (j, t) => t.IsLambdaLike, "Not a lambda");
			RegTypeCheck("Iterator", (j, t) => t.IsIteratorLike, "Not an iterator");
			RegTypeCheck("Enum", (j, t) => t.IsEnum, "Not an enum");
			RegTypeCheck("TypeDef", (j, t) => j.Runtime.IsTypeDef(t), "Not a typedef");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var type = j.GetPassingParam(0);
				if (!j.Runtime.IsTypeDef(type))
					throw new RuntimeException("GetNameOf expected a typedef");
				j.PassReturn(new Variable(j.Runtime.GetTypeName(type)));
				j.CheckIn(type);
			}, "GetNameOf", "Type");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(2);
				var type = j.GetPassingParam(0);
				var obj = j.GetPassingParam(1);
				if (!j.Runtime.IsTypeDef(type))
					throw new RuntimeException("GetCheckError expected a typedef");

				var result = j.RunTypeCheck(type, obj);

				j.PassReturn(result ?? Variable.NULL);
				j.CheckIn(type, obj);
			}, "GetCheckError", "Type");


			runtime.InjectDynamicLibrary(new LDebug(runtime), "Debug");
			runtime.InjectDynamicLibrary(new LScratch(runtime), "Scratch");
			runtime.InjectDynamicLibrary(new LPromise(runtime), "Promise");
			runtime.InjectDynamicLibrary(new LMath(runtime), "Math");
			runtime.InjectDynamicLibrary(new LPipe(runtime), "Pipe");
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

		class LPipe
		{
			ORuntime _run;
			Dictionary<OPipe, int> _activePipes = new Dictionary<OPipe, int>();
			Dictionary<OPipe, TaskCompletionSource<bool>> _waiters = new Dictionary<OPipe, TaskCompletionSource<bool>>();
			FastStack<OPipe> _reserve = new FastStack<OPipe>();

			public LPipe(ORuntime run)
			{
				_run = run;

				run.InjectExternalCall((j, t) =>
				{
					var pipe = t.Value.GetExternalContents() as OPipe;
					lock (pipe)
					{
						bool isLive = _IsLive(pipe);
						var data = pipe.PopContents();
						if (isLive)
							isLive = data.HasValue;
						else
							data = Variable.NULL;
						j.PassReturn(data, new Variable(isLive));
					}
				}, "TryRead", "Pipe");
			}

			public OPipe Create()
			{
				OPipe pip;
				lock (_activePipes)
				{
					if (_reserve.IsEmpty)
						pip = new OPipe();
					else
						pip = _reserve.Pop();
					_activePipes.Add(pip, pip.GenerationID);
				}
				return pip;

			}
			[WingraMethod]
			public void Write(OPipe pipe, Variable data)
			{
				lock (pipe)
				{
					if (!_IsLive(pipe))
						throw new CatchableError();
					pipe.Contents.Dispose(_run.Heap);
					pipe.Contents = data;
					ResolveWait(pipe, true);
				}
			}

			[WingraMethod]
			public void Kill(OPipe pipe)
			{
				lock (pipe)
				{
					pipe._gen++;
					pipe.PopContents().Dispose(_run.Heap);
					lock (_activePipes)
					{
						if (!_activePipes.Remove(pipe))
							throw new RuntimeException("pipe is already killed");
						_reserve.Push(pipe);
						ResolveWait(pipe, false);
					}
				}
			}

			void ResolveWait(OPipe pipe, bool success)
			{
				lock (_waiters)
					if (_waiters.ContainsKey(pipe))
					{
						var tcs = _waiters[pipe];
						_waiters.Remove(pipe);
						tcs.TrySetResult(success);
					}
			}

			bool _IsLive(OPipe pipe)
			{
				lock (_activePipes)
					return _activePipes.ContainsKey(pipe) && _activePipes[pipe] == pipe.GenerationID;
			}

			[WingraMethod]
			public bool IsLive(OPipe pipe)
				=> _IsLive(pipe);

			[WingraMethod]
			public bool HasData(OPipe pipe)
				=> _IsLive(pipe) && pipe.Contents.HasValue;

			[WingraMethod]
			public void Clear(OPipe pipe)
			{
				lock (pipe)
					pipe.PopContents().Dispose(_run.Heap);
			}

			[WingraMethod]
			public async Task<Variable> ReadAsync(OPipe pipe)
			{
				TaskCompletionSource<bool> tcs;
				lock (pipe)
				{
					if (pipe.Contents.HasValue)
						return pipe.PopContents();
					if (!_IsLive(pipe))
						return Variable.NULL;
					lock (_waiters)
					{
						if (!_waiters.ContainsKey(pipe))
							_waiters.Add(pipe, new TaskCompletionSource<bool>());
						tcs = _waiters[pipe];
					}
				}
				if (!await tcs.Task)
					return Variable.NULL;
				lock (pipe)
					return pipe.PopContents();
			}

			public class OPipe : IManageReference
			{
				public int _gen = 0;
				public int GenerationID { get => _gen; set => _gen = value; }
				public Variable Contents = Variable.DISPOSED;

				public Variable PopContents()
				{
					var temp = Contents;
					Contents = Variable.DISPOSED;
					return temp;
				}
			}
		}
	}
}
