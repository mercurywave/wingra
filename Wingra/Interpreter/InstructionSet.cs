using Wingra.Parser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Interpreter
{
	class InstructionSet
	{
		public static InstructionSet Static = new InstructionSet();
		Map<eAsmCommand, Operation> _index = new Map<eAsmCommand, Operation>();
		Dictionary<eAsmCommand, Func<AssemblyCodeLine, int>> _pops = new Dictionary<eAsmCommand, Func<AssemblyCodeLine, int>>();
		Dictionary<eAsmCommand, Func<AssemblyCodeLine, int>> _pushes = new Dictionary<eAsmCommand, Func<AssemblyCodeLine, int>>();

		public InstructionSet()
		{

			#region push literals
			//TODO: see if these catpure the whole asm object
			Register(eAsmCommand.PushInt, i => 0, o => 1, asm => j =>
			{
				j.Registers.Push(new Variable(asm[0].Param));
			});

			Register(eAsmCommand.PushString, i => 0, o => 1, asm => j =>
			{
				j.Registers.Push(new Variable(asm[0].Literal));
			});

			Register(eAsmCommand.PushBool, i => 0, o => 1, asm => j =>
			{
				j.Registers.Push(new Variable(asm[0].Param == 1));
			});

			Register(eAsmCommand.PushFloat, i => 0, o => 1, asm => j =>
			{
				j.Registers.Push(new Variable(asm[0].FloatLiteral));
			});

			Register(eAsmCommand.PushNull, i => 0, o => 1, asm => j =>
			{
				j.Registers.Push(Variable.NULL);
			});
			#endregion




			#region variables
			Register(eAsmCommand.Load, i => 0, o => 1, asm =>
			{
				// inlining assumes that load is basically the only way to load a local variable
				// if I copy this for some weird reason, the inliner will need to account for this
				var name = asm.Lines[0].Literal;
				var index = asm.Function.IndexOfLocal(name);
				if (index >= 0) return j =>
					j.Registers.Push(j.CurrentScope.GetLocalByIndex(index).DuplicateAsRef());
				return j =>
					j.ThrowObject(new Variable("variable not found: " + name)); // probably shouldn't happen...
			});
			Register(eAsmCommand.ShadowLoad, i => 0, o => 1, asm =>
			{
				var name = asm.Lines[0].Literal;
				var index = asm.Function.IndexOfLocal(name);
				if (index >= 0) return j =>
				{
					j.Registers.Push(j.CurrentScope.GetLocalByIndex(index).DuplicateRaw());
					j.CurrentScope.SaveNewLocal(index, new Variable());
				};
				throw new CompilerException("variable not found: " + name, asm[0].FileLine);
			});

			Register(eAsmCommand.StoreLocal, i => 1, o => 0, asm =>
			{
				var name = asm.Lines[0].Literal;
				var index = asm.Function.IndexOfLocal(name);
				if (index < 0) throw new Exception("variable not accounted for in static local variable list? " + name);
				return j =>
					j.UpdateLocal(index, j.Registers.Pop());
			});

			//TODO: this probably isn't neccessary, I don't want to leak
			Register(eAsmCommand.StoreNewLocal, i => 1, o => 0, asm =>
			{
				var name = asm.Lines[0].Literal;
				var index = asm.Function.IndexOfLocal(name);
				if (index < 0) throw new Exception("variable not accounted for in static local variable list? " + name);
				return j =>
					j.UpdateLocal(index, j.Registers.Pop());
			});

			Register(eAsmCommand.ReplaceOrNewLocal, i => 1, o => 0, asm =>
			{
				var name = asm.Lines[0].Literal;
				var index = asm.Function.IndexOfLocal(name);
				if (index < 0) throw new Exception("variable not accounted for in static local variable list? " + name);
				return j =>
					j.UpdateLocal(index, j.Registers.Pop());
			});

			// perf optimization saves a local, but leave it in the stack
			//TODO: unused?
			Register(eAsmCommand.StoreNewLocalRetain, i => 0, o => 0, asm =>
			{
				var name = asm.Lines[0].Literal;
				var index = asm.Function.IndexOfLocal(name);
				if (index < 0) throw new Exception("variable not accounted for in static local variable list?");
				return j =>
					j.UpdateLocal(index, j.Registers.Peek().DuplicateAsRef());
			});

			Register(eAsmCommand.StoreProperty, i => 2, o => 0, asm =>
			{
				var name = asm.Lines[0].Literal;
				return j =>
				{
					var target = j.Registers.Pop();
					var value = j.Registers.Pop();
					target.SetChild(new Variable(name), value, j.Heap);
					j.CheckIn(target);
				};
			});

			Register(eAsmCommand.FreeProperty, i => 1, o => 1, asm =>
			{
				var name = asm.Lines[0].Literal;
				return j =>
				{
					var target = j.Registers.Pop();
					var obj = target.FreePopChild(new Variable(name));
					j.CheckIn(target);
					j.Registers.Push(obj);
				};
			});

			Register(eAsmCommand.ReserveLocal, i => 0, o => 0, asm =>
			{
				var name = asm[0].Literal;
				return j => j.CurrentScope.ReserveLocal(name);
			});

			Register(eAsmCommand.FreeLocal, i => 0, o => 1, asm =>
			{
				var name = asm[0].Literal;
				return j =>
				{
					var obj = j.CurrentScope.FreePopVar(name);
					if (!obj.OwnsHeapContent)
					{
						// put the stuff back to make it easier to debug
						j.CurrentScope.UpdateLocal(name, obj, j.Heap);
						throw new RuntimeException("can't free local that isn't owned");
					}
					j.Registers.Push(obj);
				};
			});

			Register(eAsmCommand.SoftFreeLocal, i => 0, o => 1, asm =>
			{
				var name = asm[0].Literal;
				return j =>
				{
					var obj = j.CurrentScope.FreePopVar(name);
					j.Registers.Push(obj);
				};
			});

			Register(eAsmCommand.SetFileContext, i => 0, o => 1, asm =>
			{
				// always preceeds a scratch access/save/free
				// TODO: optimize
				var key = asm.Lines[0].Literal;
				return j =>
					j.Registers.Push(new Variable(key));
			});

			Register(eAsmCommand.StoreNewScratch, i => 2, o => 0, asm =>
			{
				// used during new, where you set initial properties inline
				var name = asm.Lines[0].Literal;
				return j =>
				{
					var key = j.Registers.Pop().AsString();
					var value = j.Registers.Pop();
					j.Runtime.ScratchScopes[key].Set(name, value);
				};
			});
			Register(eAsmCommand.ReserveScratch, i => 1, o => 0, asm =>
			{
				var name = asm[0].Literal;
				return j =>
				{
					var key = j.Registers.Pop().AsString();
					j.Runtime.ScratchScopes[key].Set(name, new Variable());
				};
			});

			Register(eAsmCommand.LoadScratch, i => 1, o => 1, asm =>
			{
				var name = asm[0].Literal;
				return j =>
				{
					var key = j.Registers.Pop().AsString();
					j.Registers.Push(j.Runtime.ScratchScopes[key].GetOrReserve(name).DuplicateAsRef());
				};
			});

			Register(eAsmCommand.FreeScratch, i => 1, o => 1, asm =>
			{
				// used during new, where you set initial properties inline
				var name = asm.Lines[0].Literal;
				return j =>
				{
					var key = j.Registers.Pop().AsString();
					var obj = j.Runtime.ScratchScopes[key].KillPop(name);
					j.Registers.Push(obj);
				};
			});
			#endregion



			#region comparisons
			Register(eAsmCommand.Equals, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var left, out var right);
				var comp = left.ContentsEqual(right);
				j.Registers.Push(new Variable(comp));
				j.CheckIn(left, right);
			});

			Register(eAsmCommand.NotEquals, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var left, out var right);
				var comp = left.ContentsEqual(right);
				j.Registers.Push(new Variable(!comp));
				j.CheckIn(left, right);
			});

			Register(eAsmCommand.LessThan, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var left, out var right);
				j.Registers.Push(new Variable(left.CompareTo(right) < 0));
				j.CheckIn(left, right);
			});

			Register(eAsmCommand.GreaterThan, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var left, out var right);
				j.Registers.Push(new Variable(left.CompareTo(right) > 0));
				j.CheckIn(left, right);
			});

			Register(eAsmCommand.EqLess, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var left, out var right);
				j.Registers.Push(new Variable(left.CompareTo(right) <= 0));
				j.CheckIn(left, right);
			});

			Register(eAsmCommand.EqGreater, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var left, out var right);
				j.Registers.Push(new Variable(left.CompareTo(right) >= 0));
				j.CheckIn(left, right);
			});

			Register(eAsmCommand.ExceedInDirection, i => 2, i => 1, asm =>
			{
				// super specialized for for loops - compare two values based on a direction variable
				var name = asm[0].Literal;
				return j =>
				{
					PopPop(j, out var left, out var right);
					var dir = j.FindVarOrThrow(name).AsInt();
					var comp = left.CompareTo(right);
					if (dir > 0)
						j.Registers.Push(new Variable(comp > 0));
					else
						j.Registers.Push(new Variable(comp < 0));
					j.CheckIn(left, right);
				};
			});

			Register(eAsmCommand.TestIfUninitialized, i => 0, o => 1, asm =>
			{
				var name = asm[0].Literal;
				return j =>
				{
					var found = j.FindVar(name);
					var result = (found == null || !found.Value.HasValue);
					j.Registers.Push(new Variable(result));
				};
			});
			Register(eAsmCommand.AssertOwnedVar, i => 0, o => 0, asm =>
			{
				var name = asm[0].Literal;
				return j =>
				{
					var found = j.FindVar(name);
					if (found != null && found.Value.HasValue && found.Value.IsPointer)
						throw new RuntimeException("parameter " + name + " was passed a reference and not the owned instance", j);
				};
			});

			Register(eAsmCommand.IsNull, i => 1, o => 1, asm => j =>
			{
				var pop = j.Registers.Pop();
				j.Registers.Push(new Variable(!pop.HasValue));
				j.CheckIn(pop);
			});
			Register(eAsmCommand.HasValue, i => 1, o => 1, asm => j =>
			{
				var pop = j.Registers.Pop();
				j.Registers.Push(new Variable(pop.HasValue));
				j.CheckIn(pop);
			});

			Register(eAsmCommand.Meh, i => 1, o => 1, asm => j =>
			{
				var orig = j.Registers.Peek();
				orig.FlagAsMeh();
				j.Registers.ReplaceTop(orig);
			});
			#endregion


			#region math
			void DoMath(Job job, eAsmCommand cmd)
			{
				PopPop(job, out var left, out var right);
				var calc = DoMathVar(cmd, left, right);
				job.Registers.Push(calc);
				job.CheckIn(left, right);
			}
			Variable DoMathVar(eAsmCommand cmd, Variable left, Variable right)
			{
				if (left.IsInt && right.IsInt)
					return new Variable(DoIntMath(left, right, cmd));
				else if (left.IsNumeric && right.IsNumeric)
					return new Variable(DoFloatMath(left, right, cmd));
				else if (cmd == eAsmCommand.Add && left.IsString && right.IsString)
					return new Variable(left.AsString() + right.AsString());
				else if (left.CanAutoConvert || right.CanAutoConvert)
				{
					if (left.CanAutoConvert && right.CanAutoConvert)
					{
						if (left.IsInt)
							return new Variable(DoIntMath(left.AsIntLoose(), right.AsIntLoose(), cmd));
						if (left.IsFloat)
							return new Variable(DoFloatMath(left.AsFloatLoose(), right.AsFloatLoose(), cmd));
						if (left.IsString)
							return new Variable(left.AsStringLoose() + right.AsStringLoose());
					}
					if (right.CanAutoConvert)
					{
						if (left.IsInt)
							return new Variable(DoIntMath(left.AsInt(), right.AsIntLoose(), cmd));
						if (left.IsFloat)
							return new Variable(DoFloatMath(left.AsFloat(), right.AsFloatLoose(), cmd));
						if (left.IsString)
							return new Variable(left.AsString() + right.AsStringLoose());
					}
					if (left.CanAutoConvert)
					{
						if (right.IsInt)
							return new Variable(DoIntMath(left.AsIntLoose(), right.AsInt(), cmd));
						if (right.IsFloat)
							return new Variable(DoFloatMath(left.AsFloatLoose(), right.AsFloat(), cmd));
						if (right.IsString)
							return new Variable(left.AsStringLoose() + right.AsString());
					}
				}
				throw new RuntimeException("cannot " + cmd.ToString() + " " + left.ToString() + " and " + right.ToString());

			}

			void RegisterMath(eAsmCommand cmd)
			{
				Register(cmd, i => 2, o => 1, asm => j => DoMath(j, cmd));
			}

			RegisterMath(eAsmCommand.Add);
			RegisterMath(eAsmCommand.Subtract);
			RegisterMath(eAsmCommand.Multiply);
			RegisterMath(eAsmCommand.Divide);
			#endregion


			#region bool logic
			Register(eAsmCommand.And, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var left, out var right);
				j.Registers.Push(new Variable(left.AsBool() && right.AsBool())); // technically this short circuits, but not in a useful way I'll regret
				j.CheckIn(left, right);
			});
			Register(eAsmCommand.Or, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var left, out var right);
				j.Registers.Push(new Variable(left.AsBool() || right.AsBool())); // technically this short circuits, but not in a useful way I'll regret
				j.CheckIn(left, right);
			});
			Register(eAsmCommand.Not, i => 1, o => 1, asm => j =>
			{
				var test = j.Registers.Pop();
				j.Registers.Push(new Variable(!test.AsBool()));
				j.CheckIn(test);
			});
			Register(eAsmCommand.NullCoalesce, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var left, out var right);
				j.Registers.Push(left.HasValue ? left : right);
				j.CheckIn(left, right);
			});
			Register(eAsmCommand.Pop, i => 1, o => 0, asm => j =>
			{
				var pop = j.Registers.Pop();
				j.CheckIn(pop); // given the current use, this shouldn't actually do anything
			});
			#endregion



			#region conditional statements
			Register(eAsmCommand.DoIfTest, i => 1, o => 0, asm =>
			{
				var target = asm.FindNextStackLevelLine();
				return j =>
				{
					var test = j.Registers.Pop();
					if (!test.AsBool())
						j.JumpShort(target);
					j.CheckIn(test);
				};
			});
			Register(eAsmCommand.LoopIfTest, i => 1, o => 0, asm =>
			{
				// for interpreter, this is identical to a DoIfTest, but transpiler needs distinction
				var target = asm.FindNextStackLevelLine();
				return j =>
				{
					var test = j.Registers.Pop();
					if (!test.AsBool())
						j.JumpShort(target);
					j.CheckIn(test);
				};
			});
			Register(eAsmCommand.ShortCircuitTrue, i => 0, o => 0, asm =>
			{
				var target = asm.FindNextStackLevelLine();
				return j =>
				{
					if (j.Registers.Peek().AsBool())
						j.JumpShort(target);
				};
			});
			Register(eAsmCommand.ShortCircuitFalse, i => 0, o => 0, asm =>
			{
				var target = asm.FindNextStackLevelLine();
				return j =>
				{
					if (!j.Registers.Peek().AsBool())
						j.JumpShort(target);
				};
			});
			Register(eAsmCommand.ShortCircuitPropNull, i => 1, o => 1, asm =>
			{
				var prop = asm[0].Literal;
				var target = asm.FindNextStackLevelLine();
				return j =>
				{
					var obj = j.Registers.Peek();
					if (!obj.HasValue || !obj.HasChildKey(prop))
					{
						j.Registers.ReplaceTop(Variable.NULL);
						j.JumpShort(target);
					}
				};
			});
			Register(eAsmCommand.ShortCircuitNull, i => 0, o => 0, asm =>
			{
				var target = asm.FindNextStackLevelLine();
				return j =>
				{
					var obj = j.Registers.Peek();
					if (!obj.HasValue)
					{
						j.Registers.ReplaceTop(Variable.NULL);
						j.JumpShort(target);
					}
				};
			});
			Register(eAsmCommand.ShortCircuitNotNull, i => 1, o => 1, asm =>
			{
				var target = asm.FindNextStackLevelLine();
				return j =>
				{
					if (j.Registers.Peek().HasValue)
						j.JumpShort(target);
				};
			});

			Register(eAsmCommand.Jump, i => 0, o => 0, asm =>
			{
				var target = asm.FindNextStackLevelLine(asm[0].Param);
				return j => j.JumpShort(target);
			});
			#endregion


			#region loops
			Register(eAsmCommand.LoopBegin, i => 0, i => 0, asm =>
			{
				var target = asm.SkipIntoStackLevel(asm.Lines[0].Param);
				return j => j.JumpShort(target);
			});
			Register(eAsmCommand.Break, i => 0, o => 0, asm =>
			{
				var target = asm.FindNextStackLevelLine(asm[0].Param);
				return j => j.JumpShort(target);
			});
			Register(eAsmCommand.Continue, i => 0, o => 0, asm =>
			{
				var target = asm.FindPrevStackLevelLine(asm[0].Param);
				return j => j.JumpShort(target);
			});
			#endregion


			#region lambdas
			Register(eAsmCommand.CreateStaticFuncPointer, i => 0, o => 1, asm =>
			{
				var path = asm[0].Literal;
				return j =>
				{
					var resolve = j.Runtime.LoadStatic(path);
					j.Registers.Push(resolve);
				};
				//TODO: bake
			});

			Register(eAsmCommand.CreateLambda, i => 0, o => 1, asm =>
			{
				var label = asm[0].Literal;
				// PERF: I think I could pre-calculate the relevant variables to copy
				return j => j.Registers.Push(j.MakeLambda(j.Code.FileCode[label]));
			});

			Register(eAsmCommand.CreateManualLambda, i => 0, o => 1, asm =>
			{
				var label = asm[0].Literal;
				return j => j.Registers.Push(new Variable(j.Code.FileCode[label], j.Heap));
			});

			Register(eAsmCommand.CaptureVar, i => 0, o => 0, asm =>
			{
				var ident = asm[0].Literal;
				return j =>
				{
					var lamb = j.Registers.Peek().GetLambdaInternal() as LambdaPointer;
					var curr = j.CurrentScope.TryFindVar(ident);
					var idx = lamb._code._localVarIndex[ident]; // I could technically precalculate this
					if (curr.HasValue)
						lamb.SaveToClosure(j.Heap, idx, curr.Value);
				};
			});

			Register(eAsmCommand.CaptureCopy, i => 0, o => 0, asm =>
			{
				var ident = asm[0].Literal;
				return j =>
				{
					var lamb = j.Registers.Peek().GetLambdaInternal() as LambdaPointer;
					var curr = j.CurrentScope.TryFindVar(ident);
					var idx = lamb._code._localVarIndex[ident]; // I could technically precalculate this
					if (curr.HasValue)
						lamb.SaveToClosure(j.Heap, idx, curr.Value.DeepCopy(j.Heap));
				};
			});

			Register(eAsmCommand.CaptureFree, i => 0, o => 0, asm =>
			{
				var ident = asm[0].Literal;
				return j =>
				{
					var lamb = j.Registers.Peek().GetLambdaInternal() as LambdaPointer;
					var obj = j.CurrentScope.FreePopVar(ident);
					if (!obj.OwnsHeapContent)
					{
						// put the stuff back to make it easier to debug
						j.CurrentScope.UpdateLocal(ident, obj, j.Heap);
						throw new RuntimeException("can't free local for capture that isn't owned");
					}
					var idx = lamb._code._localVarIndex[ident]; // I could technically precalculate this
					lamb.SaveToClosure(j.Heap, idx, obj);
				};
			});

			Register(eAsmCommand.CaptureFreeish, i => 0, o => 0, asm =>
			{
				var ident = asm[0].Literal;
				return j =>
				{
					var lamb = j.Registers.Peek().GetLambdaInternal() as LambdaPointer;
					var obj = j.CurrentScope.FreePopVar(ident);
					var idx = lamb._code._localVarIndex[ident]; // I could technically precalculate this
					lamb.SaveToClosure(j.Heap, idx, obj);
				};
			});

			#endregion

			#region function calls
			Register(eAsmCommand.PassParams, i => i.Param, o => 0, asm =>
			{
				var toPass = asm[0].Param;
				return j => j.PassParams(toPass);
			});

			Register(eAsmCommand.IgnoreParams, i => 0, o => 0, asm => j =>
			{
				j.ClearParameters();
			});

			Register(eAsmCommand.CallPathFunc, i => 0, o => 0, asm =>
			{
				var abs = asm[0].Literal;
				var path = StaticMapping.GetPathFromAbsPath(abs);
				return j =>
				{
					var glo = j.Runtime.LoadStaticGlobal(path);
					var lamb = glo.GetLambdaInternal();
					if (lamb == null) throw new RuntimeException(path + " is not a function");
					lamb.BeginExecute(j);
				};
			}); // TODO: bake

			Register(eAsmCommand.CallFileFunc, i => 0, o => 0, asm =>
			{
				var abs = asm[0].Literal;
				var arr = StaticMapping.SplitAbsPath(abs, out _, out var file);
				var path = StaticMapping.JoinPath(arr);
				return j =>
				{
					var glo = j.Runtime.LoadStaticFromFile(path, file);
					var lamb = glo.GetLambdaInternal();
					if (lamb == null) throw new RuntimeException(path + " is not a function");
					lamb.BeginExecute(j);
				};
			}); // TODO: bake


			Register(eAsmCommand.CallPathMethod, i => 1, o => 0, asm =>
			{
				var abs = asm[0].Literal;
				var path = StaticMapping.GetPathFromAbsPath(abs);
				return j =>
				{
					var pop = j.Registers.Pop();
					var glo = j.Runtime.LoadStaticGlobal(path);
					var lamb = glo.GetLambdaInternal();
					if (lamb == null) throw new RuntimeException(path + " is not a function");
					lamb.BeginExecute(j, pop);
					// j.CheckIn(pop); // can't check in, function owns the object now
				};
			}); // TODO: bake

			Register(eAsmCommand.CallFileMethod, i => 1, o => 0, asm =>
			{
				var abs = asm[0].Literal;
				var arr = StaticMapping.SplitAbsPath(abs, out _, out var file);
				var path = StaticMapping.JoinPath(arr);
				return j =>
				{
					var pop = j.Registers.Pop();
					var glo = j.Runtime.LoadStaticFromFile(path, file);
					var lamb = glo.GetLambdaInternal();
					if (lamb == null) throw new RuntimeException(path + " is not a function");
					lamb.BeginExecute(j, pop);
					//j.CheckIn(pop); // we can't check this in - the callee meeds it, and it owns the content now
				};
			}); // TODO: bake


			Register(eAsmCommand.CallFunc, i => 0, o => 0, asm =>
			{
				// expects function to read parameters immediately afterwards
				var name = asm[0].Literal;
				return j =>
				{
					var func = j.FindVarOrThrow(name);
					if (!func.IsExecutable) throw new RuntimeException(func.ToString() + " cannot be executed");
					var lamb = func.GetLambdaInternal();
					lamb.BeginExecute(j);
				};
			});
			Register(eAsmCommand.CallMethod, i => 1, o => 0, asm =>
			{
				var name = asm[0].Literal;
				return j =>
				{
					var obj = j.Registers.Pop();
					var target = obj.TryGetChild(name);
					if (target == null)
						throw new RuntimeException("obj does not have method " + name);
					var lamb = target.Value.GetLambdaInternal();
					lamb.BeginExecute(j, obj);
					//j.CheckIn(obj); // callee owns it now if it isn't just a pointer
				};
			});
			Register(eAsmCommand.ExecNamed, i => i.Param + 1, o => 0, asm =>
			{
				var toPass = asm[0].Param;
				return j =>
				{
					var func = j.Registers.Pop();
					if (!func.IsExecutable) throw new RuntimeException("$() can't evalute " + func.ToString());
					var prev = j.CurrentScope;
					var lamb = func.GetLambdaInternal();
					List<string> names = new List<string>();
					for (int i = 0; i < toPass; i++)
						names.Add(j.Registers.Pop().AsString());
					var scp = lamb.BeginExecute(j);
					j.ClearParameters();
					if (scp == null) throw new RuntimeException("$() can't inject variables into a " + func.ToString());
					if (toPass > 0 && !scp.Source.AllowInjection) throw new RuntimeException("variable injection isn't allowed into this scope");
					scp.InjectFromScope(names, prev, j.Heap);
					j.CheckIn(func); // don't need to worry about freeing the variable name strings, as they don't have allocations
				};
			});

			Register(eAsmCommand.DeclareFunction, i => 1, o => 0, asm =>
			{
				var label = asm[0].Literal;
				return j =>
				{
					var name = j.Registers.Pop().AsString();
					var code = j.Code.FileCode[label];
					j.Code.FileCode.NamedFunctions.Add(name, code);
					j.Code.FileCode.SaveConstant(name, new Variable(code), j.Heap);
				};
			});

			Register(eAsmCommand.DeclareStaticFunction, i => 0, o => 1, asm =>
			{
				var label = asm[0].Literal;
				return j =>
				{
					var code = j.Code.FileCode[label];
					var func = new Variable(code);
					func.FlagAsData();
					j.Registers.Push(func);
				};
			});

			//PERF: very good candidate for patterns
			Register(eAsmCommand.ReadParam, i => 0, o => 0, asm =>
			{
				var name = asm[0].Literal;
				var idx = asm.Function.IndexOfLocal(name); // shouldn't fail - it's a parameter, it must by in the known scope
				return j =>
				{
					if (j.HasMoreParameters() && !j.CurrentScope._allocatedLocals[idx].HasValue)
					{
						var obj = j.ReadNextParameter();
						j.CurrentScope._allocatedLocals[idx] = obj;
					}
				};
			});
			Register(eAsmCommand.ReadMultiParam, i => 0, o => 0, asm =>
			{
				var name = asm[0].Literal;
				var idx = asm.Function.IndexOfLocal(name);
				return j =>
				{
					if (!j.CurrentScope._allocatedLocals[idx].HasValue)
					{
						var obj = j.ReadRemainingParameters();
						j.CurrentScope._allocatedLocals[idx] = obj;
					}
				};
			});

			Register(eAsmCommand.Return, i => i.Param, o => 0, asm =>
			{
				var toPop = asm[0].Param;
				var except = asm.Function.GetReturnIdxs(toPop);
				return j =>
				{
					j.FreeScopeMemory(except);
					j.PassReturn(toPop);
					j.UnwindStack(false); // already freed
				};
			});

			Register(eAsmCommand.Quit, i => 0, o => 0, asm =>
			{
				var toReturn = asm.Function.GetReturnIdxs();
				return j =>
				{
					j.FreeScopeMemory(toReturn);
					var arr = toReturn.Select(i => j.CurrentScope.GetLocalByIndex(i));
					j.PassReturn(arr.ToArray());
					j.UnwindStack(false);
				};
			});

			Register(eAsmCommand.ReadReturn, i => 0, o => o.Param, asm =>
			{
				int toRead = asm[0].Param;
				if (toRead == 1) return j => j.ReadReturn();
				return j => j.ReadReturn(toRead);
			});


			#endregion


			#region async/await
			Register(eAsmCommand.ARunCode, i => 1, o => 1, asm => j =>
			{
				var func = j.Registers.Pop();
				if (!func.IsExecutable) throw new RuntimeException(func.ToString() + " cannot be executed");
				var pop = func.GetLambdaInternal();

				var lamb = pop as LambdaPointer;
				var task = j.Runtime.QueueBackground(lamb._code, lamb._closure);

				j.Registers.Push(new Variable(task.ID));
			});
			#endregion


			#region data
			Register(eAsmCommand.StoreToData, i => 1, o => 0, asm => j =>
			{
				var obj = j.Registers.Pop();
				obj.FlagAsData();
				// obj may not have any c# references left and get garbage collected
				// probably should have added it to a registry if you didn't want that to happen I suppose?
				// might end up being an adventageous leak? Maybe should add it to a real list somewhere?
			});

			Register(eAsmCommand.StoreToFileConst, i => 1, o => 0, asm =>
			{
				var path = asm[0].Literal;
				return j =>
				{
					var obj = j.Registers.Pop();
					j.Code.FileCode.SaveConstant(path, obj, j.Heap);
				};
			});

			Register(eAsmCommand.StoreToPathData, i => 1, o => 0, asm =>
			{
				var path = asm[0].Literal;
				return j =>
				{
					var obj = j.Registers.Pop();
					// file key isn't really relevant here
					j.Runtime.LoadStaticVar(path, obj);
				};
			});

			Register(eAsmCommand.StoreEnumToFileConst, i => 1, o => 0, asm =>
			{
				var path = asm[0].Literal;
				return j =>
				{
					var obj = j.Registers.Pop();
					obj = new Variable(path, obj, j.Heap);
					j.Code.FileCode.SaveConstant(path, obj, j.Heap);
				};
			});

			Register(eAsmCommand.StoreEnumToPathData, i => 1, o => 0, asm =>
			{
				var path = asm[0].Literal;
				return j =>
				{
					var obj = j.Registers.Pop();
					obj = new Variable(path, obj, j.Heap);
					// file key isn't really relevant here
					j.Runtime.LoadStaticVar(path, obj);
				};
			});

			Register(eAsmCommand.LoadPathData, i => 0, o => 1, asm =>
			{
				var path = asm[0].Literal;
				return j =>
				{
					var obj = j.Runtime.LoadStaticGlobal(path);
					j.Registers.Push(obj.MakePointer());
				};
			}, asm =>
			{
				var path = asm[0].Literal;
				return j => j.Runtime.IsStaticGlobalInitialized(path);
			});

			Register(eAsmCommand.LoadPathFile, i => 0, o => 1, asm =>
			{
				var path = util.Piece(asm[0].Literal, "|", 1);
				var file = util.Piece(asm[0].Literal, "|", 2);
				return j =>
				{
					var obj = j.Runtime.LoadConstantFromFile(path, file);
					j.Registers.Push(obj.MakePointer());
				};
			}, asm =>
			{
				var path = util.Piece(asm[0].Literal, "|", 1);
				var file = util.Piece(asm[0].Literal, "|", 2);
				return j =>
					j.Runtime.TryLoadConstantFromFile(path, file).HasValue;
			});
			#endregion

			#region objects
			Register(eAsmCommand.DimArray, i => 0, o => 1, asm =>
			{
				var cap = asm[0].Param;
				return j =>
				{
					var list = j.Heap.CheckOutList(cap);
					var obj = new Variable(list, j.Heap);
					j.Registers.Push(obj);
				};
			});

			Register(eAsmCommand.DimDictionary, i => 0, o => 1, asm =>
			{
				var toPass = asm[0].Param;
				return j =>
				{
					var list = j.Heap.CheckOut(toPass, false, false, false);
					var obj = new Variable(list, j.Heap);
					j.Registers.Push(obj);
				};
			});

			Register(eAsmCommand.SetupMixin, i => 0, o => 1, asm =>
			{
				return j =>
				{
					// this is a slightly silly shim because I don't want to make
					// a bunch of different calling function commands just for mixins
					var obj = j.Registers.Peek();
					j.Registers.Push(obj.DuplicateAsRef());
				};
			});

			Register(eAsmCommand.DimSetInt, i => 1, o => 0, asm =>
			{
				var toPass = asm[0].Param;
				return j =>
				{
					var pop = j.Registers.Pop();
					var dim = j.Registers.Peek();
					var inner = dim.GetStruct();
					inner.SetChild(toPass, pop, j.Heap);
				};
			});

			Register(eAsmCommand.DimSetString, i => 1, o => 0, asm =>
			{
				var toPass = asm[0].Literal;
				return j =>
				{
					var pop = j.Registers.Pop();
					var dim = j.Registers.Peek();
					var inner = dim.GetStruct();
					inner.SetChild(toPass, pop, j.Heap);
				};
			});

			Register(eAsmCommand.DimSetExpr, i => 2, o => 0, asm =>
			{
				return j =>
				{
					var key = j.Registers.Pop();
					var val = j.Registers.Pop();
					var dim = j.Registers.Peek();
					var inner = dim.GetStruct();
					inner.SetChild(key, val, j.Heap);
				};
			});

			Register(eAsmCommand.DotAccess, i => 1, o => 1, asm =>
			{
				var name = asm[0].Literal;
				return j =>
				{
					var obj = j.Registers.Pop();
					var child = obj.TryGetChild(name);
					if (child == null)
						throw new RuntimeException("object does not have property " + name);
					j.Registers.Push(child.Value.DuplicateAsRef());
					j.CheckIn(obj);
				};
			});
			Register(eAsmCommand.Has, i => 1, o => 1, asm =>
			{
				var name = asm[0].Literal;
				return j =>
				{
					var obj = j.Registers.Pop();
					var found = obj.TryGetChild(name) != null;
					j.Registers.Push(new Variable(found));
					j.CheckIn(obj);
				};
			});
			Register(eAsmCommand.Is, i => 2, o => 1, asm => j =>
			{
				PopPop(j, out var obj, out var type);
				j.Registers.Push(new Variable(true));
				var lamb = type.GetLambdaInternal();
				lamb.BeginExecute(j, obj);
				j.CheckIn(obj, type);
			});
			Register(eAsmCommand.Copy, i => 1, o => 1, asm => j =>
			{
				var orig = j.Registers.Pop();
				j.Registers.Push(orig.DeepCopy(j.Heap));
				j.CheckIn(orig);
			});
			Register(eAsmCommand.PushPeekDup, i => 0, o => 1, asm => j =>
			{
				var orig = j.Registers.Peek();
				j.Registers.Push(orig.DuplicateAsRef());
			});

			Register(eAsmCommand.KeyAccess, i => i.Param + 1, o => 1, asm =>
			{
				var count = asm[0].Param;
				return j =>
				{
					var keys = j.Registers.PopReverse(count);
					var obj = j.Registers.Pop();
					var target = obj;
					bool found = true;
					foreach (var k in keys)
					{
						var temp = target.TryGetChild(k);
						if (!temp.HasValue)
						{
							found = false;
							break;
						}
						target = temp.Value;
					}
					if (found) j.Registers.Push(target.DuplicateAsRef());
					else j.Registers.Push(new Variable());
					j.CheckIn(keys.ToArray());
					j.CheckIn(obj);
				};
			});

			Register(eAsmCommand.KeyAssign, i => i.Param + 2, o => 0, asm =>
			{
				var depth = asm[0].Param;
				return j =>
				{
					var keys = j.Registers.PopReverse(depth);
					var obj = j.Registers.Pop();
					var value = j.Registers.Pop();
					var target = obj;

					// fill in intermediate keys
					for (int i = 0; i < keys.Count - 1; i++)
					{
						var kCurr = keys[i];
						var kNext = keys[i + 1];
						var temp = target.TryGetChild(kCurr);
						if (!temp.HasValue)
						{
							var list = j.Heap.CheckOutStructForKey(kNext);
							temp = new Variable(list, j.Heap);
							target.SetChild(kCurr, temp.Value, j.Heap);
						}
						target = temp.Value;
					}

					var kFinal = keys[keys.Count - 1];
					target.SetChild(kFinal, value, j.Heap);

					j.CheckIn(keys.ToArray());
					j.CheckIn(obj);
				};
			});

			Register(eAsmCommand.KeyFree, i => i.Param + 1, o => 1, asm =>
			{
				var depth = asm[0].Param;
				return j =>
				{
					var keys = j.Registers.PopReverse(depth);
					var obj = j.Registers.Pop();
					var target = obj;

					// fill in intermediate keys
					bool found = TryWalkKeys(keys, ref target, keys.Count - 1);

					Variable child;
					if (found)
					{
						// I probably should 
						var kFinal = keys[keys.Count - 1];
						child = target.FreePopChild(kFinal);
					}
					else child = new Variable();
					j.Registers.Push(child);

					j.CheckIn(keys.ToArray());
					j.CheckIn(obj);
				};
			});

			Register(eAsmCommand.SoftFreeKey, i => i.Param + 1, o => 1, asm =>
			{
				var depth = asm[0].Param;
				return j =>
				{
					var keys = j.Registers.PopReverse(depth);
					var obj = j.Registers.Pop();
					var target = obj;

					// fill in intermediate keys
					bool found = TryWalkKeys(keys, ref target, keys.Count - 1);

					Variable child;
					if (found)
					{
						var kFinal = keys[keys.Count - 1];
						child = target.FreePopChild(kFinal);
					}
					else child = new Variable();
					j.Registers.Push(child);

					j.CheckIn(keys.ToArray());
					j.CheckIn(obj);
				};
			});


			Register(eAsmCommand.LoadFirstKey, i => 1, o => 1, asm => j =>
			{
				var obj = j.Registers.Pop();
				// ASSUMES: don't need to worry about check out because keys can never be structures
				j.Registers.Push(obj.GetFirstKey(j.Heap));
				j.CheckIn(obj);
			});
			Register(eAsmCommand.LoadLastKey, i => 1, o => 1, asm => j =>
			{
				var obj = j.Registers.Pop();
				// ASSUMES: don't need to worry about check out because keys can never be structures
				j.Registers.Push(obj.GetLastKey(j.Heap));
				j.CheckIn(obj);
			});
			Register(eAsmCommand.LoadNextKey, i => 2, o => 1, asm => j =>
			{
				var key = j.Registers.Pop();
				var obj = j.Registers.Pop();
				// ASSUMES: don't need to worry about check out because keys can never be structures
				j.Registers.Push(obj.GetNextKey(key, j.Heap));
				j.CheckIn(obj);
			});
			#endregion


			#region errors
			Register(eAsmCommand.CreateErrorTrap, i => 0, o => 0, asm =>
			{
				var target = asm.FindNextStackLevelLine(asm[0].Param);
				return j => j.CurrentScope._errorTrapJump = target;
			});
			Register(eAsmCommand.ThrowError, i => i.Param, o => 0, asm =>
			{
				if (asm[0].Param == 0)
					return j => j.ThrowObject();
				return j =>
				{
					Variable obj = j.Registers.Pop();
					j.ThrowObject(obj);
				};
			});
			Register(eAsmCommand.ClearErrorTrap, i => 0, o => 0, asm =>
			{
				var target = asm[0].Param;
				if (target >= 0) target = asm.FindNextStackLevelLine(target);
				return j => j.CurrentScope._errorTrapJump = target;
			});
			Register(eAsmCommand.FatalError, i => 0, o => 0, asm => j =>
			{
				j.ThrowFatalError();
			});
			#endregion

			#region defer
			Register(eAsmCommand.FlagDefer, i => 0, o => 0, asm =>
			{
				var name = asm[0].Literal;
				var index = asm.Function.IndexOfLocal(name);
				return j =>
				{
					var cur = j.CurrentScope.GetLocalByIndex(index);
					if (cur.HasValue)
						throw new RuntimeException("defer can only be triggered once");
					j.UpdateLocal(index, new Variable(true));
				};
			});
			Register(eAsmCommand.RunDeferIfSet, i => 0, o => 0, asm =>
			{
				var name = asm[0].Literal;
				var index = asm.Function.IndexOfLocal(name);
				var target = asm.FindNextStackLevelLine();
				return j =>
				{
					var cur = j.CurrentScope.GetLocalByIndex(index);
					if (!cur.HasValue)
						j.JumpShort(target);
				};
			});
			#endregion


			#region iterators
			Register(eAsmCommand.IterCreate, i => 1, o => 1, asm => j =>
			{
				var pop = j.Registers.Peek();
				if (pop.IsIteratorLike)
				{
					pop.GetIteratorInternal().MoveNext(j, j.Heap);
				}
				else
				{
					j.Registers.Pop();
					if (!pop.IsStructLike) // TODO:
						throw new RuntimeException("cannot iterate on " + pop.ToString());
					var contents = j.Heap.CheckOutStructIterator();
					contents.Initialize(pop, j.Heap);
					var iter = new Variable(contents, j.Heap);
					j.Registers.Push(iter.DuplicateRaw());

					// we don't check in because the iterator is the new owner in the case that we have the real object
					// if it was a pointer, we don't really care about check in, so there's no case that needs it
					//j.CheckIn(pop); 
				}
			});
			Register(eAsmCommand.IterIsComplete, i => 1, o => 1, asm => j =>
			{
				// TODO: we know the name of the iterator, we shouldn't need to push/pull it off the stack
				var pop = j.Registers.Pop();
				if (!pop.IsIteratorLike) throw new RuntimeException("not an iterator - " + pop.ToString());
				var pointer = pop.GetIteratorInternal();
				j.Registers.Push(new Variable(pointer.IsComplete));
				j.CheckIn(pop);
			});
			Register(eAsmCommand.IterLoadCurrent, i => 1, o => 1, asm => j =>
			{
				var pop = j.Registers.Pop();
				if (!pop.IsIteratorLike) throw new RuntimeException("not an iterator - " + pop.ToString());
				var pointer = pop.GetIteratorInternal();
				j.Registers.Push(pointer.GetCurrent().DuplicateAsRef());
				j.CheckIn(pop);
			});
			Register(eAsmCommand.IterLoadCurrPacked, i => 1, o => o.Param, asm =>
		   {
			   var toRead = asm[0].Param;
			   return j =>
			   {
				   var pop = j.Registers.Pop();
				   if (!pop.IsIteratorLike)
					   throw new RuntimeException("not an iterator - " + pop.ToString());
				   var pointer = pop.GetIteratorInternal();
				   var packed = pointer.GetCurrentPacked();
				   if (toRead > packed.Count)
					   throw new RuntimeException("expected " + toRead + " returns, iterator returned " + packed.Count);
				   for (int i = 0; i < toRead; i++)
					   j.Registers.Push(packed[packed.Count - toRead + i].DuplicateAsRef());
				   j.CheckIn(pop);
			   };
		   });

			Register(eAsmCommand.IterMoveNext, i => 1, o => 0, asm => j =>
			{
				var pop = j.Registers.Pop();
				if (!pop.IsIteratorLike) throw new RuntimeException("not an iterator - " + pop.ToString());
				var pointer = pop.GetIteratorInternal();
				pointer.MoveNext(j, j.Heap);
				j.CheckIn(pop);
			});

			Register(eAsmCommand.PreYield, i => 0, o => 0, asm =>
			{
				int toPass = asm[0].Param;
				var iterIdx = asm.Function.IndexOfLocal(Consts.ITERATOR_VAR);
				return j =>
				{
					var it = j.Heap.CheckOutCodeIterator();
					it.Initialize(j.CurrentScope, j.Heap, toPass);
					var obj = new Variable(it, j.Heap);
					j.CurrentScope.SaveNewLocal(iterIdx, obj.MakePointer());
					j.PassReturn(obj);
					j.CallStack.Kill(1);
				};
			});

			Register(eAsmCommand.YieldIterator, i => i.Param, o => 0, asm =>
			{
				int toPass = asm[0].Param;
				var iterIdx = asm.Function.IndexOfLocal(Consts.ITERATOR_VAR);
				return j =>
				{
					var obj = j.CurrentScope.GetLocalByIndex(iterIdx);
					var pt = obj.GetIteratorInternal();
					var iter = pt._iterator as OCodeIterator;
					var ret = j.Registers.Pop(toPass);
					iter.UpdateCurrent(ret);
					j.CallStack.Kill(1);
				};
			});


			Register(eAsmCommand.YieldFinalize, i => 0, o => 0, asm =>
			{
				var iterIdx = asm.Function.IndexOfLocal(Consts.ITERATOR_VAR);
				return j =>
				{
					var obj = j.CurrentScope.GetLocalByIndex(iterIdx);
					var pt = obj.GetIteratorInternal();
					var iter = pt._iterator as OCodeIterator;
					iter._complete = true;
					j.CallStack.Kill(1);
				};
			});

			#endregion


			#region misc
			Register(eAsmCommand.ClearRegisters, i => i.Param, o => 0, asm =>
			{
				var count = asm[0].Param;
				return j =>
				{
					for (int i = 0; i < count; i++)
					{
						var pop = j.Registers.Pop();
						j.CheckIn(pop);
					}
				};
			});

			Register(eAsmCommand.NoOp);
			Register(eAsmCommand.BeginAwaitCall); // needed for transpiling

			#endregion

			foreach (var e in util.EnumOptions<eAsmCommand>())
				if (!_pushes.ContainsKey(e))
					throw new NotImplementedException("command not registered: " + e.ToString());

		}

		private static bool TryWalkKeys(List<Variable> keys, ref Variable target, int steps = -1)
		{
			bool found = true;
			if (steps < 0) steps = keys.Count;
			for (int i = 0; i < steps; i++)
			{
				var kCurr = keys[i];
				var kNext = keys[i + 1];
				var temp = target.TryGetChild(kCurr);
				if (!temp.HasValue)
				{
					found = false;
					break;
				}
				target = temp.Value;
			}

			return found;
		}

		int DoIntMath(Variable left, Variable right, eAsmCommand cmd)
			=> DoIntMath(left.AsInt(), right.AsInt(), cmd);
		int DoIntMath(int a, int b, eAsmCommand cmd)
		{
			switch (cmd)
			{
				case eAsmCommand.Add: return a + b;
				case eAsmCommand.Subtract: return a - b;
				case eAsmCommand.Multiply: return a * b;
				case eAsmCommand.Divide: return a / b;
				default: throw new NotImplementedException();
			}
		}

		float DoFloatMath(Variable left, Variable right, eAsmCommand cmd)
			=> DoFloatMath(left.AsFloat(), right.AsFloat(), cmd);
		float DoFloatMath(float a, float b, eAsmCommand cmd)
		{
			switch (cmd)
			{
				case eAsmCommand.Add: return a + b;
				case eAsmCommand.Subtract: return a - b;
				case eAsmCommand.Multiply: return a * b;
				case eAsmCommand.Divide: return a / b;
				default: throw new NotImplementedException();
			}
		}

		public List<OpChainEvaluator> GetOpMatches(eAsmCommand init)
		{
			return _index.Values(init).Select(
				op => new OpChainEvaluator(op.Pattern, op.Generator)
				).ToList();
		}
		public bool IsNoOp(eAsmCommand init) => !_index.Values(init).Any() && _pops.ContainsKey(init);

		#region util
		void PopPop(Job j, out Variable left, out Variable right)
		{
			right = j.Registers.Pop();
			left = j.Registers.Pop();
		}

		class Operation
		{
			public OpPattern Pattern;
			public Func<InstructionContext, Instruction> Generator;
			public Operation(OpPattern pat, Func<InstructionContext, Instruction> gen) { Pattern = pat; Generator = gen; }
		}

		OpPattern sing(eAsmCommand cmd) => new OpPattern(cmd);
		OpPattern rpt(eAsmCommand cmd) => new OpPattern(cmd) { Type = OpPattern.eType.Repeat };
		OpPattern any() => new OpPattern(eAsmCommand.NoOp) { Type = OpPattern.eType.Any };

		void Register(eAsmCommand cmd
			, Func<AssemblyCodeLine, int> pop, Func<AssemblyCodeLine, int> push
			, Func<InstructionContext, Action<Job>> fallback)
		{
			_pops.Add(cmd, pop);
			_pushes.Add(cmd, push);
			Register(sing(cmd), fallback);
		}
		void Register(eAsmCommand cmd
			, Func<AssemblyCodeLine, int> pop, Func<AssemblyCodeLine, int> push
			, Func<InstructionContext, Action<Job>> fallback
			, Func<InstructionContext, Func<Job, bool>> canRun)
		{
			_pops.Add(cmd, pop);
			_pushes.Add(cmd, push);
			Register(sing(cmd), fallback, canRun);
		}

		void Register(eAsmCommand cmd)
		{
			// basically just for no-ops
			_pops.Add(cmd, i => 0);
			_pushes.Add(cmd, o => 0);
		}

		void Register(OpPattern pattern, Func<InstructionContext, Action<Job>> act)
		{
			_index.Add(pattern.Match, new Operation(pattern, asm => new Instruction(act(asm))));
		}

		void Register(OpPattern pattern, Func<InstructionContext, Action<Job>> fallback, Func<InstructionContext, Func<Job, bool>> canRun)
		{
			_index.Add(pattern.Match, new Operation(pattern, asm => new Instruction(fallback(asm), canRun(asm))));
		}
		#endregion

		#region push/pop
		public static int CalcPush(AssemblyCodeLine line)
			=> Static._pushes[line.Command].Invoke(line);

		public static int CalcPop(AssemblyCodeLine line)
			=> Static._pops[line.Command].Invoke(line);

		public static int CalcDelta(AssemblyCodeLine line)
			=> CalcPush(line) - CalcPop(line);

		public static void GetStackManip(AssemblyCodeLine line, out int pop, out int push)
		{
			pop = CalcPop(line);
			push = CalcPush(line);
		}

		#endregion

	}

	class InstructionContext
	{
		public List<AssemblyCodeLine> Lines;
		public AssemblyCode Function;
		public AssemblyCodeLine this[int j] => Lines[j];
		int _lineOffset;

		public InstructionContext(AssemblyCode func, List<AssemblyCodeLine> asm, int assemblyStart)
		{
			Lines = asm;
			Function = func;
			_lineOffset = assemblyStart;
		}

		public int FindNextStackLevelLine() => FindNextStackLevelLine(Lines[0].AssemblyStackLevel);
		public int FindNextStackLevelLine(int stack) => Function.FindNextStackLevelLinePredicted(_lineOffset, stack);

		public int FindPrevStackLevelLine() => FindPrevStackLevelLine(Lines[0].AssemblyStackLevel);
		public int FindPrevStackLevelLine(int stack) => Function.FindPrevStackLevelLinePredicted(_lineOffset, stack);

		public int SkipIntoStackLevel(int stack) => Function.FindSkipIntoDepthPredicted(_lineOffset, stack);

		public AssemblyCodeLine FindPreviousInstructionLine(eAsmCommand command)
			=> Function.FindPreviousInstructionLine(_lineOffset, command);
	}
}
