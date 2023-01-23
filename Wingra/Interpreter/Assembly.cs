using Wingra.Parser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Wingra.Interpreter
{
	public class FileAssembler // TODO: does this need to be public?
	{
		public string Name;
		public string Key;
		public HashSet<string> RequiredSymbols = new HashSet<string>();
		public HashSet<string> ExportedSymbols = new HashSet<string>();
		public FileAssembler(string name, string key)
		{
			Name = name;
			Key = key;
			FuncDefRoutine = new FunctionFactory(Key, "***functions");
			RegistryRoutine = new FunctionFactory(key, "***registry");
			StructureRoutine = new FunctionFactory(Key, "***data");
			StaticInitRoutine = new FunctionFactory(Key, "***static");
			InitRoutine = new FunctionFactory(Key, Consts.INIT_FUNC);
		}

		public FunctionFactory FuncDefRoutine;
		public FunctionFactory RegistryRoutine;
		public FunctionFactory StructureRoutine;
		public FunctionFactory StaticInitRoutine;
		public FunctionFactory InitRoutine;

		internal Dictionary<string, FunctionFactory> _funcs = new Dictionary<string, FunctionFactory>();

		public FunctionFactory GenFunction(string name) => _GenFunction("::" + name);
		public FunctionFactory GenLambda() => _GenFunction("_func", true, true);
		public FunctionFactory GenLambda(int fileline) => _GenFunction("_func" + fileline, true, true);
		public FunctionFactory GenLambda(int fileline, bool allowUndefined)
			=> _GenFunction("_func" + fileline, allowUndefined, true);
		FunctionFactory _GenFunction(string name, bool allowUndefined = false, bool allowInjection = false)
		{
			var func = GenPossibleFunction(name);
			if (allowUndefined) func.AllowUndefined = true;
			if (allowInjection) func.AllowInjection = true;
			RegisterFunction(func);
			return func;
		}
		//if you aren't sure whether a function is actually needed (e.g. ctor on an object)
		public FunctionFactory GenPossibleFunction(string name)
		{
			name = "" + ((_funcs.Count + 1) * 13) + name;
			var func = new FunctionFactory(Key, name);
			return func;
		}
		public void RegisterFunction(FunctionFactory func) => _funcs.Add(func.UniqNameInFile, func);

		//assemble all functions into a single list of lines
		public AssemblyFile Assemble(Compiler compiler, ErrorLogger errors)
		{
			var file = new AssemblyFile(Name, Key);
			if (RequiredSymbols.Any()) file.RequiredSymbols = RequiredSymbols;
			if (ExportedSymbols.Any()) file.ExportedSymbols = ExportedSymbols;

			if (FuncDefRoutine.HasCode) file.FunctionFunc = FuncDefRoutine.Assemble(compiler, this, errors);
			if (RegistryRoutine.HasCode) file.RegistryFunc = RegistryRoutine.Assemble(compiler, this, errors);
			if (StructureRoutine.HasCode) file.StructureFunc = StructureRoutine.Assemble(compiler, this, errors);
			if (StaticInitRoutine.HasCode) file.StaticInitFunc = StaticInitRoutine.Assemble(compiler, this, errors);
			if (InitRoutine.HasCode) file.InitFunc = InitRoutine.Assemble(compiler, this, errors);

			foreach (var pair in _funcs)
				file.Add(pair.Key, pair.Value.Assemble(compiler, this, errors));

			return file;
		}
		public override string ToString()
		{
			return Name + "::" + Key;
		}
	}


	// all code end points in a file
	// special startup functions are not included in the collection directly
	public class AssemblyFile : Dictionary<string, AssemblyCode>
	{
		public string Name;
		public string Key;
		public AssemblyCode FunctionFunc = null;
		public AssemblyCode RegistryFunc = null;
		public AssemblyCode StructureFunc = null;
		public AssemblyCode StaticInitFunc = null;
		public AssemblyCode InitFunc = null;
		public HashSet<string> RequiredSymbols = null;
		public HashSet<string> ExportedSymbols = null;

		public AssemblyFile(string name, string key) { Name = name; Key = key; }

		internal const string FUNCTION_ROUTINE = "***funcs";
		internal const string REGISTRY_ROUTINE = "***registry";
		internal const string STRUCT_ROUTINE = "***struct";
		internal const string STATIC_INIT_ROUTINE = "***static";
		internal const string INIT_ROUTINE = "***init";

		// PERF: maybe constant string access should use a different path? is that already happening?
		internal AssemblyCode GetByName(string funcName)
		{
			if (funcName == FUNCTION_ROUTINE) return FunctionFunc;
			if (funcName == REGISTRY_ROUTINE) return RegistryFunc;
			if (funcName == STRUCT_ROUTINE) return StructureFunc;
			if (funcName == STATIC_INIT_ROUTINE) return StaticInitFunc;
			if (funcName == INIT_ROUTINE) return InitFunc;
			return this[funcName];
		}

		public IEnumerable<KeyValuePair<string, AssemblyCode>> AllDebugCode()
		{
			foreach (var key in LoadKeys())
			{
				var func = GetByName(key);
				if (func != null)
					yield return new KeyValuePair<string, AssemblyCode>(key, func);
			}
			foreach (var pair in this)
				yield return pair;
		}

		public static IEnumerable<string> LoadKeys()
		{
			yield return FUNCTION_ROUTINE;
			yield return STATIC_INIT_ROUTINE;
			yield return REGISTRY_ROUTINE;
			yield return STRUCT_ROUTINE;
			yield return INIT_ROUTINE;
		}

		public override string ToString()
		{
			return "{" + Name + "}";
		}
	}


	public class AssemblyCode : List<AssemblyCodeLine>
	{
		public int[] PredictedInstructionIndex; // populated by CodeBlock
		public string FileKey;
		public bool AllowInjection;
		public bool IsAsync;

		public AssemblyCode(string filekey, bool allowInjection, bool isAsync)
		{
			FileKey = filekey;
			AllowInjection = allowInjection;
			IsAsync = isAsync;
		}

		public int FindNextStackLevelLinePredicted(int line, int stack)
			=> PredictedInstructionIndex[FindNextStackLevelLine(line, stack)];
		// one line after previous line at stack
		public int FindPrevStackLevelLinePredicted(int line, int stack)
			=> PredictedInstructionIndex[FindPrevStackLevelLine(line, stack)];
		public int FindSkipIntoDepthPredicted(int line, int stack)
			=> PredictedInstructionIndex[FindSkipIntoDepth(line, stack)];

		public int FindNextStackLevelLine(int line, int stack)
		{
			for (int i = line + 1; i < this.Count; i++)
				if (this[i].AssemblyStackLevel <= stack)
					return i;
			throw new Exception("Could not find stack level " + stack + " after assemby line " + line);
		}
		// one line after previous line at stack
		public int FindPrevStackLevelLine(int line, int stack)
		{
			for (int i = line - 1; i >= 0; i--)
				if (this[i].AssemblyStackLevel <= stack)
					return i + 1;
			throw new Exception("Could not find stack level " + stack + " prior assemby line " + line);
		}
		// pretty specifically for loop stuff
		public int FindSkipIntoDepth(int line, int stack)
		{
			for (int i = line + 1; i < this.Count; i++)
				if (this[i].AssemblyStackLevel >= stack)
					return i;
			throw new Exception("Could not find stack level " + stack + " after assemby line " + line);
		}

		public AssemblyCodeLine FindPreviousInstructionLine(int line, eAsmCommand command)
		{
			for (int i = line - 1; i >= 0; i--)
				if (this[i].Command == command)
					return this[i];
			throw new Exception("Could not find " + command + " prior assemby line " + line);
		}

		List<string> _localVars;
		internal HashSet<string> _assumedVariables;
		internal HashSet<string> _injectableParams;
		Dictionary<string, int> _localMap;
		List<int> _returnIndices;
		internal List<string> LocalVariables => _localVars;
		internal int IndexOfLocal(string loc)
		{
			if (!_localMap.ContainsKey(loc)) return -1;
			return _localMap[loc];
		}

		public bool DoesYield() => _localMap.ContainsKey(Consts.ITERATOR_VAR);

		internal List<int> GetReturnIdxs()
			=> GetReturnIdxs(_returnIndices.Count);
		public int GetReturnCount => _returnIndices.Count;

		internal List<int> GetReturnIdxs(int numReturns)
		{
			if (_returnIndices.Count == 0 || numReturns == 0) return new List<int>();
			return _returnIndices.GetRange(0, numReturns);
		}


		List<OpCondenser> _operationPlan = new List<OpCondenser>();
		internal List<OpCondenser> OperationPlan => _operationPlan;

		// code may combine lines -- expects the raw stack pointer
		public int GetFileLineFromCodeLine(int codeLine)
		{
			int asmLine = GetAssemblyLineFromCodeLine(codeLine);
			if (asmLine < 0) return asmLine;
			return this[asmLine].FileLine;
		}

		public int GetAssemblyLineFromCodeLine(int codeLine)
		{
			for (int i = 0; i < PredictedInstructionIndex.Length; i++)
				if (PredictedInstructionIndex[i] == codeLine)
					return i;
			return -1;
		}

		public void Finalize(List<string> variables, List<string> returnParams, HashSet<string> assumedVariables, HashSet<string> injectableParams)
		{
			_localVars = variables;
			_assumedVariables = assumedVariables;
			_injectableParams = injectableParams;
			_localMap = new Dictionary<string, int>();
			for (int i = 0; i < _localVars.Count; i++)
			{
				if (_localMap.ContainsKey(_localVars[i]))
					throw new CompilerException("variable " + _localVars[i] + " declared twice?", -1);
				_localMap.Add(_localVars[i], i);
			}

			_returnIndices = new List<int>(returnParams.Count);
			foreach (var nm in returnParams)
				_returnIndices.Add(IndexOfLocal(nm));

			PredictedInstructionIndex = new int[Count];
			for (int i = 0; i < Count; i++)
				PredictedInstructionIndex[i] = -1;

			for (int i = 0; i < Count;)
			{
				var cmd = this[i].Command;
				if (InstructionSet.Static.IsNoOp(cmd))
				{
					PredictedInstructionIndex[i] = _operationPlan.Count;
					i++;
					continue;
				}
				var matches = InstructionSet.Static.GetOpMatches(cmd);
				Debug.Assert(matches.Count > 0); // haven't implemented this command!
				var plan = ReadAhead(i, matches);

				for (int j = 0; j < plan.Assembly.Count; j++)
					PredictedInstructionIndex[j + i] = _operationPlan.Count;

				_operationPlan.Add(plan);

				i += plan.Assembly.Count;
			}
		}

		OpCondenser ReadAhead(int beginAt, List<OpChainEvaluator> matches)
		{
			List<OpCondenser> possible = new List<OpCondenser>();
			for (int i = beginAt; i <= Count; i++)
			{
				var peek = i < Count ? this[i].Command : eAsmCommand.NoOp;
				foreach (var m in matches)
					if (m.Chain.PeekMatch(peek) == OpChain.eMatch.Complete)
						possible.Add(new OpCondenser(GetRange(beginAt, i - beginAt), m, beginAt));
				matches.RemoveAll(m => m.Chain.State == OpChain.eMatch.Reject || m.Chain.State == OpChain.eMatch.Complete);
				if (matches.Count == 0) break;
			}
			Debug.Assert(possible.Count > 0);
			OpCondenser opPlan = possible[0];
			// it's possible that two plans have the same pattern length
			// e.g. rpt(op) / sing(op) - where there is only one
			// so should generally place the repeats below the single case, or delete the single case
			for (int j = 0; j < possible.Count; j++)
				if (possible[j].Assembly.Count > opPlan.Assembly.Count)
					opPlan = possible[j];
			return opPlan;
		}

		#region debug print

		public string DebugVars()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("  vars: ");
			foreach (var ident in _localVars)
			{
				if (_assumedVariables.Contains(ident))
					sb.Append("[" + ident + "], ");
				else sb.Append(ident + ", ");
			}
			return sb.ToString();
		}

		public string DebugPrint(int bcLine, bool wide = false)
		{
			//TODO: doesn't account for float literals
			var line = this[bcLine];
			StringBuilder sb = new StringBuilder();
			sb.Append(RJustify("" + bcLine, 6));
			sb.Append(RJustify("" + line.FileLine, 6));
			sb.Append(RJustify("" + line.AssemblyStackLevel, 4));
			sb.Append(RJustify("" + line.Command.ToString(), 20));
			sb.Append(RJustify("" + line.Param, 9));
			var str = util.BoundedSubstr(line.Literal.Replace("\n", "\\n"), 0, wide ? 80 : 10);
			str = LJustify(str, 10);
			sb.Append("  " + str);
			return sb.ToString();
		}

		public static string DebugPrintHeader()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(RJustify("off ", 6));
			sb.Append(RJustify("ln ", 6));
			sb.Append(RJustify("stk", 4));
			sb.Append(RJustify("cmd", 20));
			sb.Append(RJustify("param", 9));
			sb.Append("  literal");
			return sb.ToString();
		}

		static string RJustify(string str, int width)
		{
			if (str.Length >= width) return str;
			return util.RepeatString(" ", width - str.Length) + str;
		}
		static string LJustify(string str, int width)
		{
			if (str.Length >= width) return str;
			return str + util.RepeatString(" ", width - str.Length);
		}

		#endregion
	}
	public class FunctionFactory
	{
		List<AssemblyCodeLine> _lines = new List<AssemblyCodeLine>();
		public string UniqNameInFile;
		public string FileKey;
		List<string> _returnParams = new List<string>();
		internal bool AllowUndefined = false;
		internal bool AllowInjection = false;
		internal bool IsAsync;
		internal bool CanThrow;
		SortedDictionary<int, IDefer> _defers = new SortedDictionary<int, IDefer>();
		SortedDictionary<int, int> _insertDefers = new SortedDictionary<int, int>();
		int _writePointer = -1;
		int WritePointer => _writePointer < 0 ? _lines.Count : _writePointer;

		public FunctionFactory() { UniqNameInFile = ""; } // primarily for appending to another function
		public FunctionFactory(string fileKey, string name) { UniqNameInFile = name; FileKey = fileKey; }

		public void Add(int asmStackLevel, eAsmCommand command, int param = 0, string literal = "", float floatLiteral = 0)
		{
			CheckUnregisterVar(asmStackLevel);
			if (CmdDefinesNewVar(command))
				RegisterVar(literal, WritePointer, asmStackLevel);
			else if (CmdAccessesVar(command))
				// I'd _prefer_ the caller to assert before this, but sometimes that is awkward :(
				_AssertVarDefined(literal, null);
			else if (CmdSavesVar(command))
				_AssertVarDefined(literal, null);
			_escapes.Pop(asmStackLevel);
			_tests.ClearTest(asmStackLevel);
			_Add(asmStackLevel, command, param, literal, floatLiteral);
			if (command == eAsmCommand.BeginAwaitCall)
			{
				if (AllowUndefined)
					IsAsync = true;
				else if (!IsAsync)
					throw new CompilerException("await can only be used in async function", CurrentFileLine);
			}
		}

		void _Add(int asmStackLevel, eAsmCommand command, int param = 0, string literal = "", float floatLiteral = 0)
		{
			var acl = new AssemblyCodeLine(CurrentFileLine, asmStackLevel, command, param, literal, floatLiteral);
			_lines.Insert(WritePointer, acl);
			if (_writePointer >= 0)
				_writePointer++;
		}
		public void Add(int asmStackLevel, eAsmCommand command, string literal)
			=> Add(asmStackLevel, command, 0, literal);
		public void Add(int asmStackLevel, eAsmCommand command, float literal)
			=> Add(asmStackLevel, command, 0, "", literal);
		public void ClearAsmStack(int asmStackLevel)
		{
			int prev = WritePointer - 1; // why was this set to -2? is this change going to break a bunch of stuff?
			if (prev < 0 || _lines[prev].AssemblyStackLevel != asmStackLevel || _tests.WouldPop(asmStackLevel))
				Add(asmStackLevel, eAsmCommand.NoOp);
			// TODO: this is a bad hack
			//  I should handle the if/else escape cases explicitly, instead of relying on the stack level to break out
		}
		public void UnrollRegisters(int asmStackLevel, int count)
		{
			if (count > 0) Add(asmStackLevel, eAsmCommand.ClearRegisters, count);
		}

		#region variable stuff

		List<VarMap> _varMap = new List<VarMap>();
		internal HashSet<string> _declaredVars = new HashSet<string>();
		List<string> _allDeclaredVars = new List<string>();
		HashSet<string> _assumedVariables = new HashSet<string>();
		HashSet<string> _injectableParams = new HashSet<string>();
		struct VarMap
		{
			public string DeclaredName;
			public int StackLevel;
			public string ShadowName;
			public VarMap(string declaredName, int stackLevel, string shadowName)
			{
				DeclaredName = declaredName;
				StackLevel = stackLevel;
				ShadowName = shadowName;
			}
		}

		void RegisterVar(string name, int asmLine, int asmStack)
		{
			var shadow = "";
			if (_declaredVars.Contains(name) && asmLine >= 0)
			{
				shadow = GetUniqueTemp(name);
				_declaredVars.Add(shadow); // do not want this to run recursively!
				_allDeclaredVars.Add(shadow);
				_Add(asmStack, eAsmCommand.ShadowLoad, 0, name);
				_Add(asmStack, eAsmCommand.StoreNewLocal, 0, shadow);
			}
			else if (!_allDeclaredVars.Contains(name))
				_allDeclaredVars.Add(name);
			_declaredVars.Add(name);
			_varMap.Add(new VarMap()
			{
				DeclaredName = name,
				StackLevel = asmStack,
				ShadowName = shadow,
			});
			_declaredVars.Add(name);
		}
		void CheckUnregisterVar(int asmStack)
		{
			var removing = _varMap.Where(v => v.StackLevel > asmStack).ToArray();
			if (removing.Length == 0) return;

			_varMap.RemoveAll(v => v.StackLevel > asmStack);

			foreach (var v in removing)
			{
				// these need to not go recursive
				if (v.ShadowName != "")
				{
					_Add(asmStack, eAsmCommand.ShadowLoad, 0, v.ShadowName);
					_Add(asmStack, eAsmCommand.StoreLocal, 0, v.DeclaredName);
				}
				else
				{
					//PERF: I could probably remove this in an optimized build if this knew it was optimized
					//		that might be a problem if a variable is reserved without value inside a loop, though
					_Add(asmStack, eAsmCommand.SoftFreeLocal, 0, v.DeclaredName);
					_Add(asmStack, eAsmCommand.ClearRegisters, 1); // free pushes the value to the stack
					_declaredVars.Remove(v.DeclaredName);
				}
			}
		}

		// effectively, looks backwards from current line until we hit the toAsmLevel and declares them at a higher level
		// trap @a : $Foo()
		// we want a to be declared at the ourter scope, and not inside the error trap
		public void HoistDeclaredVars(int fromAsmLevel, int toAsmLevel)
		{
			var removing = _varMap.Where(v => v.StackLevel == fromAsmLevel).ToArray();
			_varMap.RemoveAll(v => v.StackLevel == fromAsmLevel);

			foreach (var v in removing)
				_varMap.Add(new VarMap(v.DeclaredName, toAsmLevel, v.ShadowName));
		}

		public void ReserveVariable(string token)
			=> RegisterVar(token, -1, 0);
		public void DeclareVariable(string name, int asmStackLevel)
			=> RegisterVar(name, WritePointer, asmStackLevel);

		void _AssertVarDefined(string name, RelativeTokenReference? toke)
		{
			if (_declaredVars.Contains(name)) return;
			if (AllowUndefined || AllowInjection)
			{
				_assumedVariables.Add(name);
				ReserveVariable(name);
			}
			else
				throw new CompilerException("Variable not defined: " + name, CurrentFileLine, toke);
		}
		internal void AssertVarDefined(RelativeTokenReference toke)
			=> _AssertVarDefined(toke.Token.Token, toke);

		public void ManuallyAssumeVariable(string name)
		{
			if (_declaredVars.Contains(name)) return;
			_assumedVariables.Add(name);
			ReserveVariable(name);
		}

		public void RegisterInjectableParameter(string name)
			=> _injectableParams.Add(name);

		#endregion

		#region deferrals
		internal void RegisterDefer(IDefer defer)
		{
			if (_writePointer >= 0)
				throw new CompilerException("cannot nest deferrals", CurrentFileLine);
			_defers.Add(WritePointer, defer);
		}
		List<IDefer> GetDeferalsForLine(int idx)
		{
			idx = FindIndexOfLoopOrCurrent(idx);
			List<IDefer> list = new List<IDefer>();
			foreach (var pair in _defers)
				if (pair.Key <= idx)
					list.Add(pair.Value);
			return list;
		}
		internal void InjectDeferrals(int asmStackLevel)
		{
			if (_writePointer >= 0)
				throw new CompilerException("cannot nest returns in defer", CurrentFileLine);
			_insertDefers.Add(WritePointer, asmStackLevel);
		}
		int FindIndexOfLoopOrCurrent(int idx)
		{
			// we want to find the line at which defers could potentially have run
			// find bottom of loop if in one
			if (idx >= _lines.Count) return idx;
			int depth = _lines[idx].AssemblyStackLevel;
			for (int i = idx; i >= 0; i--)
			{
				var ln = _lines[i];
				if (ln.AssemblyStackLevel > depth) continue;
				if (ln.AssemblyStackLevel < depth) depth = ln.AssemblyStackLevel;
				if (depth == 0) break;
				if (CmdBeginsLoop(ln.Command))
				{
					//where does this loop end?
					for (int j = i + 1; j < _lines.Count; j++)
						if (_lines[j].AssemblyStackLevel <= ln.AssemblyStackLevel)
							return j;
					return _lines.Count - 1;
				}
			}
			// otherwise, return current line
			return idx;
		}
		void EmitDeferals(int injectAtIdx, Compiler compiler, FileAssembler file, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_writePointer = injectAtIdx;
			var toRun = GetDeferalsForLine(injectAtIdx);
			foreach (var run in toRun)
				run.EmitDefer(compiler, file, this, asmStackLevel, errors, parent);
			_writePointer = -1;
		}
		#endregion

		public bool IsInErrorTrap()
		{
			for (int i = WritePointer - 1; i >= 0; i--)
			{
				var cmd = _lines[i].Command;
				if (cmd == eAsmCommand.CreateErrorTrap)
					return true;
				if (cmd == eAsmCommand.ClearErrorTrap)
					return false;
			}
			return false;
		}

		// this is super fragile. it basically is meant for clear error trap param
		public int FindParentErrorTrap()
		{
			int scope = 2; // we expect to find the current trap scope, we want the one before that
			for (int i = WritePointer - 1; i >= 0; i--)
			{
				var cmd = _lines[i].Command;
				if (cmd == eAsmCommand.CreateErrorTrap)
				{
					scope--;
					if (scope == 0)
						return _lines[i].Param;
				}
				if (cmd == eAsmCommand.ClearErrorTrap)
					scope++;
			}
			return -1;
		}

		// asummes defined return names, so only called once per function
		public void AddReturnParam(params string[] names)
		{
			foreach (var nm in names)
				AddSingleReturnParam(nm);
		}
		void AddSingleReturnParam(string name)
		{
			_returnParams.Add(name);
			ReserveVariable(name);
		}
		public void ReserveAnonReturnParams(int count)
		{
			for (int i = _returnParams.Count; i < count; i++)
				AddSingleReturnParam(GetUniqueTemp("return"));
		}
		public string GetReturnParamByIdx(int idx) => _returnParams[idx];
		public bool HasDefinedReturns => _returnParams.Count > 0;
		public int DefinedReturnCount => _returnParams.Count;

		bool _doesYield = false;
		public void SetupIterator(int expectedParams)
		{
			_doesYield = true;
			Add(0, eAsmCommand.PreYield, expectedParams);
			ReserveVariable(Consts.ITERATOR_VAR);
		}

		public bool HasCommand(eAsmCommand command)
			=> _lines.Any(ln => ln.Command == command);
		public bool HasCommand(eAsmCommand command, string literal)
			=> _lines.Any(ln => ln.Command == command && ln.Literal == literal);
		public bool HasCommand(eAsmCommand command, int param)
			=> _lines.Any(ln => ln.Command == command && ln.Param == param);

		public bool HasCommandAt(eAsmCommand command, string literal, int line)
			=> _lines[line].Command == command && _lines[line].Literal == literal;


		public void AddCleanup(FunctionFactory additionalOps)
		{
			_lines.AddRange(additionalOps._lines);
		}

		public bool AddInlineFunc(FunctionFactory inline, Dictionary<string, Func<FunctionFactory, eAsmCommand, int, bool>> mapper, int asmStackLevel)
		{
			foreach (var vari in inline._allDeclaredVars)
			{
				if (!mapper.ContainsKey(vari))
					DeclareVariable(vari, asmStackLevel);
			}
			foreach (var line in inline._lines)
			{
				var cmd = line.Command;
				// You can trigger this with a return switch with an expression that contains a one liner
				// it's a good argument for building an actual AST instead
				// right now this would probably need a new method to drill into scoped stuff like that
				// PERF: it would be nice if we could detect that scenario earlier, so we don't waste time like this
				//Debug.Assert(cmd != eAsmCommand.CreateLambda);
				if (cmd == eAsmCommand.CreateLambda) return false;
				string literal = line.Literal;
				if (CommandLoadsSymbol(cmd))
				{
					if (mapper.ContainsKey(line.Literal))
					{
						if (!mapper[literal].Invoke(this, cmd, asmStackLevel + line.AssemblyStackLevel))
							return false;
					}
					else // kinda a hack to handle parameters that aren't passed
					{
						//Add(line.AssemblyStackLevel, eAsmCommand.PushNull);
						ReserveVariable(line.Literal);
						Add(asmStackLevel + line.AssemblyStackLevel, cmd, line.Param, literal, line.FloatLiteral);
					}

				}
				else
					Add(asmStackLevel + line.AssemblyStackLevel, cmd, line.Param, literal, line.FloatLiteral);
			}
			return true;
		}

		static bool CommandLoadsSymbol(eAsmCommand cmd)
			=> CmdSavesVar(cmd) || CmdAccessesVar(cmd);

		public Dictionary<string, int> GetSymbolUseCount()
		{
			Dictionary<string, int> dict = new Dictionary<string, int>();
			foreach (var line in _lines)
			{
				if (!CmdAccessesVar(line.Command)) continue;
				if (dict.ContainsKey(line.Literal)) dict[line.Literal] = dict[line.Literal] + 1;
				else dict[line.Literal] = 1;
			}
			return dict;
		}

		internal void FindDependantCaptures(FileAssembler file)
		{
			foreach (var ln in _lines)
			{
				if (ln.Command == eAsmCommand.CreateLambda || ln.Command == eAsmCommand.CreateManualLambda)
				{
					var id = ln.Literal;
					if (!file._funcs.ContainsKey(id)) continue;
					var target = file._funcs[id];
					foreach (var ident in target._assumedVariables)
						_AssertVarDefined(ident, null);
				}
			}
		}
		internal void ValidateDependantCaptures(FileAssembler file)
		{
			foreach (var ln in _lines)
			{
				if (ln.Command == eAsmCommand.CreateLambda || ln.Command == eAsmCommand.CreateManualLambda)
				{
					var id = ln.Literal;
					if (!file._funcs.ContainsKey(id)) continue;
					var target = file._funcs[id];
					if (!target.AllowUndefined)
						foreach (var ident in target._assumedVariables)
							if (!_declaredVars.Contains(ident))
								throw new CompilerException("variable " + ident + " can't be captured", ln.FileLine);
				}
			}
		}

		internal AssemblyCode Assemble(Compiler compiler, FileAssembler file, ErrorLogger errors)
		{
			int line = -1;
			if (HasCode)
			{
				// first line of the function might have a line of -1
				for (int i = 0; i < _lines.Count; i++)
				{
					line = _lines[i].FileLine;
					if (line >= 0) break;
				}
				if (!CmdIsFuncExit(_lines[_lines.Count - 1].Command) || _lines[_lines.Count - 1].AssemblyStackLevel > 0)
					InjectDeferrals(0);
			}

			foreach (var pair in _insertDefers.Reverse())
				EmitDeferals(pair.Key, compiler, file, pair.Value, errors, null);

			AssemblyCode code = new AssemblyCode(FileKey, AllowInjection, IsAsync);
			//code.Add(new AssemblyCodeLine(line, 0, eAsmCommand.Label, 0, UniqNameInFile));
			code.AddRange(_lines);
			var actuallyYields = code.Any(lin => lin.Command == eAsmCommand.YieldIterator);
			var hasReturn = code.Any(lin => CmdIsFuncExit(lin.Command));

			var hasThrow = false;
			var trapDepth = 0;
			foreach (var lin in _lines)
			{
				var cmd = lin.Command;
				if (cmd == eAsmCommand.CreateErrorTrap) trapDepth++;
				else if (cmd == eAsmCommand.ClearErrorTrap) trapDepth--;
				else if (trapDepth == 0 && cmd == eAsmCommand.ThrowError)
					hasThrow = true;
			}

			if (!_doesYield && actuallyYields)
				throw new CompilerException("function must declare yield as return parameter", line);
			if (actuallyYields && hasReturn)
				throw new CompilerException("function cannot both yield and return", line);
			if (!AllowUndefined && !CanThrow && hasThrow)
				throw new CompilerException("function throws but is not indicated to throw", line);

			if (!HasCode)
				code.Add(new AssemblyCodeLine(line, 0, _doesYield ? eAsmCommand.YieldFinalize : eAsmCommand.Quit));
			else if (!CmdIsFuncExit(_lines[_lines.Count - 1].Command) || _lines[_lines.Count - 1].AssemblyStackLevel > 0)
			{
				var fileline = _lines[_lines.Count - 1].FileLine;
				code.Add(new AssemblyCodeLine(fileline, 0, _doesYield ? eAsmCommand.YieldFinalize : eAsmCommand.Quit));
			}

			if (AllowUndefined)
				FindDependantCaptures(file);
			else
				ValidateDependantCaptures(file);

			code.Finalize(FindLocals(), _returnParams, _assumedVariables, _injectableParams);
			return code;
		}

		static bool CmdIsFuncExit(eAsmCommand cmd)
			=> cmd == eAsmCommand.Return || cmd == eAsmCommand.Quit;
		static bool CmdBeginsLoop(eAsmCommand cmd)
			=> cmd == eAsmCommand.LoopBegin || cmd == eAsmCommand.LoopIfTest;
		static bool CmdDefinesNewVar(eAsmCommand cmd)
			=> (cmd == eAsmCommand.StoreNewLocal
				|| cmd == eAsmCommand.ReplaceOrNewLocal
				|| cmd == eAsmCommand.ReadParam
				|| cmd == eAsmCommand.ReadMultiParam
				|| cmd == eAsmCommand.ReserveLocal
				|| cmd == eAsmCommand.StoreNewLocalRetain);

		public static bool CmdSavesVar(eAsmCommand cmd)
			=> (cmd == eAsmCommand.StoreLocal
				|| cmd == eAsmCommand.FreeLocal
				|| cmd == eAsmCommand.SoftFreeLocal
				|| cmd == eAsmCommand.ShadowLoad);
		public static bool CmdAccessesVar(eAsmCommand cmd)
			=> cmd == eAsmCommand.Load
				|| cmd == eAsmCommand.CallFunc
				|| cmd == eAsmCommand.TestIfUninitialized
				|| cmd == eAsmCommand.AssertOwnedVar
				|| cmd == eAsmCommand.ExceedInDirection;

		internal List<string> FindLocals()
		{
			return _allDeclaredVars;
		}


		internal AssemblyCode AssembleExpression()
		{
			AssemblyCode code = new AssemblyCode(FileKey, AllowInjection, IsAsync);
			code.AddRange(_lines);
			code.Add(new AssemblyCodeLine(-1, 0, eAsmCommand.NoOp)); // if an expression end could have a short circuited tail, jumps might need a reference point
			code.Finalize(FindLocals(), _returnParams, _assumedVariables, _injectableParams);
			return code;
		}

		internal bool HasCode => _lines.Count > 0; // this gets wonky and may not be trustworthy with the dynamic return appended

		// this is a hack passing the file line through a ton of parsing code just isn't worth it
		// this is accurate in most simple cases, and generally much simpler
		public int CurrentFileLine = -1;
		public void SetFileLine(int line) => CurrentFileLine = line;

		EscapeStack _escapes = new EscapeStack();
		public void RegisterEscape(eToken toke, int stackExit, int stackContinue) => _escapes.Push(toke, stackExit, stackContinue);
		public int GetCurrentEscapeDepthBreak() => _escapes.PeekBreak();
		public int GetCurrentEscapeDepthBreak(eToken specific) => _escapes.PeekBreak(specific);
		public int GetCurrentEscapeDepthContinue() => _escapes.PeekContinue();
		public int GetCurrentEscapeDepthContinue(eToken specific) => _escapes.PeekContinue(specific);

		class EscapeStack
		{
			Stack<Tuple<eToken, int, int>> _stack = new Stack<Tuple<eToken, int, int>>();
			public void Push(eToken toke, int stackExit, int stackContinue)
				=> _stack.Push(new Tuple<eToken, int, int>(toke, stackExit, stackContinue));
			public void Pop() => _stack.Pop();
			public void Pop(int stack)
			{
				while (_stack.Count > 0 && _stack.Peek().Item2 >= stack && _stack.Peek().Item3 >= stack)
					_stack.Pop();
			}
			public int PeekBreak(eToken toke)
			{
				foreach (var pair in _stack)
					if (pair.Item1 == toke)
						return pair.Item2;
				throw new ParserException("could not break to " + toke.ToString());
			}
			public int PeekBreak() => _stack.Peek().Item2;

			public int PeekContinue(eToken toke)
			{
				foreach (var pair in _stack)
					if (pair.Item1 == toke)
						return pair.Item3;
				throw new ParserException("could not continue to " + toke.ToString());
			}
			public int PeekContinue() => _stack.Peek().Item3;
		}

		TestTracker _tests = new TestTracker();
		public void RegisterIfElse(int asmStackLevel, bool branchExhausted = false)
			=> _tests.Push(asmStackLevel, branchExhausted);
		public bool InIfElseScope(int asmStackLevel) => _tests.InTest(asmStackLevel);
		public bool IsBranchReachable(int asmStackLevel) => _tests.IsCurrentBranchReachable(asmStackLevel);

		class TestTracker
		{
			struct Test
			{
				public int AsmStackLevel;

				// any else conditions are impossible
				// set when a compile-time constant asserts a branch is unreachable
				public bool BranchesExhausted;
				public Test(int asmLvl, bool branchExhausted) { AsmStackLevel = asmLvl; BranchesExhausted = branchExhausted; }
			}
			// expected to track the +1 stack level of tests
			Stack<Test> _stack = new Stack<Test>();
			public void Push(int asmStackLevel, bool branchExhausted = false)
				=> _stack.Push(new Test(asmStackLevel, branchExhausted));
			void PopInner(int asmStackLevel)
			{
				while (_stack.Count > 0 && _stack.Peek().AsmStackLevel > asmStackLevel)
					_stack.Pop();
			}
			public void ClearTest(int asmStackLevel)
			{
				while (_stack.Count > 0 && _stack.Peek().AsmStackLevel >= asmStackLevel)
					_stack.Pop();
			}
			public bool WouldPop(int asmStackLevel)
			{
				return (_stack.Count > 0 && _stack.Peek().AsmStackLevel > asmStackLevel);
			}
			public bool InTest(int asmStackLevel)
			{
				if (_stack.Count == 0) return false;
				PopInner(asmStackLevel);
				if (_stack.Count == 0) return false;
				return (_stack.Peek().AsmStackLevel == asmStackLevel);
			}

			public bool IsCurrentBranchReachable(int asmStackLevel)
			{
				if (_stack.Count == 0) return false;
				PopInner(asmStackLevel);
				if (_stack.Count == 0) return false;
				var peek = _stack.Peek();
				return (peek.AsmStackLevel == asmStackLevel && !peek.BranchesExhausted);
			}
		}

		int _localUniq = 1;
		public string GetReserveUniqueTemp(string key)
		{
			var uniq = GetUniqueTemp(key);
			ReserveVariable(uniq);
			return uniq;
		}
		public string GetUniqueTemp(string key)
		{
			_localUniq++;
			return "%" + key + _localUniq;
		}

		public int GetReturnCount => _returnParams.Count;

		public int GetStackDelta()
			=> _lines.Sum(ln => ln.GetStackDelta());

		public override string ToString()
		{
			return UniqNameInFile + "::" + FileKey;
		}
	}

	public enum eAsmCommand
	{
		NoOp,
		StoreLocal, StoreNewLocal, ReplaceOrNewLocal, StoreProperty, StoreNewLocalRetain,
		Load, LoadPathData, LoadPathFile, LoadScratch, ShadowLoad,
		KeyAssign,
		StoreNewScratch, FreeScratch, SetFileContext,
		PushInt, PushString, PushBool, PushFloat, PushNull, PushPeekDup,
		DoIfTest, ShortCircuitTrue, ShortCircuitFalse, ShortCircuitNull, ShortCircuitPropNull, ShortCircuitNotNull,
		Equals, NotEquals, HasValue, IsNull,
		GreaterThan, LessThan, EqGreater, EqLess, ExceedInDirection,
		And, Or, NullCoalesce, // TODO: not actually needed with short circuiting
		Pop,
		Not, Meh,
		Return, ReadReturn, Quit,
		Jump, Break, // param is stack level to jump to
		Continue, // goes back to top of loop at stack level
		Add, Subtract, Multiply, Divide,
		ClearRegisters,
		DeclareFunction,
		StoreToPathData, StoreToData, StoreToFileConst,
		StoreEnumToPathData, StoreEnumToFileConst,
		PassParams, ReadParam, ReadMultiParam, IgnoreParams, BeginAwaitCall,
		CallFunc, CallMethod, ExecNamed,
		CallFileFunc, CallPathFunc,
		CallFileMethod, CallPathMethod,
		ARunCode,
		DotAccess, Has, LoadFirstKey, LoadNextKey, LoadLastKey, Copy,
		KeyAccess,
		FreeProperty, FreeLocal, KeyFree, SoftFreeLocal, SoftFreeKey,
		CreateStaticFuncPointer, CreateLambda, DeclareStaticFunction,
		CreateManualLambda, CaptureVar, CaptureCopy, CaptureFree, CaptureFreeish,
		ReserveLocal, ReserveScratch,
		DimArray, DimDictionary,
		DimSetInt, DimSetString, DimSetExpr, SetupMixin,
		PreYield, YieldIterator, YieldFinalize,
		IterCreate, IterIsComplete, IterMoveNext, IterLoadCurrent, IterLoadCurrPacked,
		LoopBegin, LoopIfTest,
		TestIfUninitialized, // pushes bool if local is reserved, used for ?:
		AssertOwnedVar, // just used to assert owned parameters
		CreateErrorTrap, ThrowError, ClearErrorTrap, FatalError,
		FlagDefer, RunDeferIfSet,
	}

	public class AssemblyCodeLine
	{
		public int AssemblyStackLevel;
		public eAsmCommand Command;
		public int Param;
		public string Literal;
		public int FileLine;
		public float FloatLiteral;

		public AssemblyCodeLine(int fileline, int stack, eAsmCommand command, int param = 0, string literal = "", float floatLiteral = 0)
		{
			FileLine = fileline;
			AssemblyStackLevel = stack;
			Command = command;
			Param = param;
			Literal = literal;
			FloatLiteral = floatLiteral;
		}

		public string DisplayString()
		{
			var literal = Literal.Replace("\n", "\\n");
			return "ln " + FileLine + "  >" + AssemblyStackLevel + " " + Command.ToString() + " " + Param + " " + literal;
		}
		public override string ToString() => DisplayString();

		public int GetStackDelta() => InstructionSet.CalcDelta(this);
		public void GetStackManip(out int pop, out int push)
			=> InstructionSet.GetStackManip(this, out pop, out push);
		public string GetCommandShort() => GetCommandShort(Command);
		static string GetCommandShort(eAsmCommand cmd)
		{
			switch (cmd)
			{
				case eAsmCommand.NoOp: return "nop";
				case eAsmCommand.StoreLocal: return "sto";
				case eAsmCommand.StoreNewLocal: return "snl";
				case eAsmCommand.ReplaceOrNewLocal: return "rnl";
				case eAsmCommand.StoreProperty: return "sp";
				case eAsmCommand.StoreNewLocalRetain: return "sret";
				case eAsmCommand.Load: return "lol";
				case eAsmCommand.LoadPathData: return "lpd";
				case eAsmCommand.LoadPathFile: return "lpf";
				case eAsmCommand.LoadScratch: return "lps";
				case eAsmCommand.ShadowLoad: return "sha";
				case eAsmCommand.KeyAssign: return "ka";
				case eAsmCommand.StoreNewScratch: return "sscr";
				case eAsmCommand.FreeScratch: return "fscr";
				case eAsmCommand.SetFileContext: return "fcont";
				case eAsmCommand.PushInt: return "int";
				case eAsmCommand.PushString: return "str";
				case eAsmCommand.PushBool: return "boo";
				case eAsmCommand.PushFloat: return "flo";
				case eAsmCommand.PushNull: return "nul";
				case eAsmCommand.DoIfTest: return "dif";
				case eAsmCommand.ShortCircuitTrue: return "stru";
				case eAsmCommand.ShortCircuitFalse: return "sfal";
				case eAsmCommand.ShortCircuitNull: return "snul";
				case eAsmCommand.ShortCircuitPropNull: return "spnul";
				case eAsmCommand.Equals: return "eq";
				case eAsmCommand.NotEquals: return "ne";
				case eAsmCommand.HasValue: return "hasv";
				case eAsmCommand.IsNull: return "isn";
				case eAsmCommand.GreaterThan: return "gt";
				case eAsmCommand.LessThan: return "lt";
				case eAsmCommand.EqGreater: return "eqg";
				case eAsmCommand.EqLess: return "eql";
				case eAsmCommand.And: return "and";
				case eAsmCommand.Or: return "or";
				case eAsmCommand.Not: return "not";
				case eAsmCommand.Meh: return "meh";
				case eAsmCommand.Return: return "ret";
				case eAsmCommand.ReadReturn: return "rret";
				case eAsmCommand.Quit: return "quit";
				case eAsmCommand.Jump: return "brk";
				case eAsmCommand.Continue: return "cont";
				case eAsmCommand.Add: return "add";
				case eAsmCommand.Subtract: return "sub";
				case eAsmCommand.Multiply: return "mul";
				case eAsmCommand.Divide: return "div";
				case eAsmCommand.ClearRegisters: return "clr";
				case eAsmCommand.DeclareFunction: return "dfunc";
				case eAsmCommand.StoreToPathData: return "spd";
				case eAsmCommand.StoreToData: return "sdat";
				case eAsmCommand.StoreToFileConst: return "sfc";
				case eAsmCommand.StoreEnumToPathData: return "sed";
				case eAsmCommand.StoreEnumToFileConst: return "sef";
				case eAsmCommand.PassParams: return "ppar";
				case eAsmCommand.ReadParam: return "rpar";
				case eAsmCommand.ReadMultiParam: return "rmul";
				case eAsmCommand.IgnoreParams: return "igpar";
				case eAsmCommand.CallFunc: return "call";
				case eAsmCommand.CallMethod: return "cmeth";
				case eAsmCommand.ExecNamed: return "exec";
				case eAsmCommand.CallFileFunc: return "cfil";
				case eAsmCommand.CallPathFunc: return "cpat";
				case eAsmCommand.CallFileMethod: return "cfm";
				case eAsmCommand.CallPathMethod: return "cpm";
				case eAsmCommand.DotAccess: return "dot";
				case eAsmCommand.Has: return "has";
				case eAsmCommand.LoadFirstKey: return "lfk";
				case eAsmCommand.LoadNextKey: return "lnk";
				case eAsmCommand.LoadLastKey: return "llk";
				case eAsmCommand.Copy: return "copy";
				case eAsmCommand.KeyAccess: return "key";
				case eAsmCommand.FreeProperty: return "fprop";
				case eAsmCommand.SoftFreeLocal: return "sfl";
				case eAsmCommand.FreeLocal: return "floc";
				case eAsmCommand.KeyFree: return "fkey";
				case eAsmCommand.SoftFreeKey: return "sfkey";
				case eAsmCommand.CreateStaticFuncPointer: return "csfp";
				case eAsmCommand.CreateLambda: return "lam";
				case eAsmCommand.DeclareStaticFunction: return "dsf";
				case eAsmCommand.ReserveLocal: return "rloc";
				case eAsmCommand.ReserveScratch: return "rscr";
				case eAsmCommand.DimArray: return "darr";
				case eAsmCommand.DimDictionary: return "ddic";
				case eAsmCommand.DimSetInt: return "dsi";
				case eAsmCommand.DimSetString: return "dss";
				case eAsmCommand.DimSetExpr: return "dse";
				case eAsmCommand.SetupMixin: return "mix";
				case eAsmCommand.PreYield: return "pyld";
				case eAsmCommand.YieldIterator: return "yld";
				case eAsmCommand.YieldFinalize: return "yfin";
				case eAsmCommand.IterCreate: return "iter";
				case eAsmCommand.IterIsComplete: return "isit";
				case eAsmCommand.IterMoveNext: return "itmov";
				case eAsmCommand.IterLoadCurrent: return "itld";
				case eAsmCommand.IterLoadCurrPacked: return "ilcp";
				case eAsmCommand.LoopBegin: return "loop";
				case eAsmCommand.TestIfUninitialized: return "isres";
				case eAsmCommand.CreateErrorTrap: return "trap";
				case eAsmCommand.ThrowError: return "trhw";
				case eAsmCommand.ClearErrorTrap: return "cerr";
				case eAsmCommand.FatalError: return "fat";
				case eAsmCommand.FlagDefer: return "fdef";
				case eAsmCommand.RunDeferIfSet: return "ddef";
				default: throw new NotImplementedException();
			}
		}
	}
}
