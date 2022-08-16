using Wingra.Interpreter;
using Wingra.Parser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Transpilers
{
	public class Javascript
	{
		WingraCompile _compile;
		WingraSymbols _symbols;

		public Javascript(WingraCompile compile, WingraSymbols symbols)
		{
			_compile = compile;
			_symbols = symbols;
		}
		public StringBuilder Output(string funcName)
		{
			StringBuilder sb = new StringBuilder();

			sb.AppendLine("function " + funcName + "(" + RUNTIME + ")");
			using (Braces(sb))
			{
				sb.AppendLine("var " + FUNCS + " = {};");
				foreach (var key in AssemblyFile.LoadKeys())
					foreach (var file in _compile.Assemblies)
					{
						var func = file.GetByName(key);
						if (func != null)
							EmitLoadFunction(sb, file, func);
					}
				sb.AppendLine();
				foreach (var file in _compile.Assemblies)
				{
					if (file.Count == 0) continue;
					sb.AppendLine(); sb.AppendLine();
					sb.AppendLine("// " + file.Key);
					sb.Append(FUNCS + "[" + GetShortFileKey(file) + "] = ");
					using (Braces(sb))
						foreach (var pair in file)
						{
							sb.Append("\"" + pair.Key + "\" : ");
							EmitFunction(sb, file, pair.Value);
							sb.Append(",\n");
						}
					sb.Append(";\n");
				}
			}

			return sb;
		}


		int _fileKeyIdx = 01;
		Dictionary<string, string> _fileKeyMap = new Dictionary<string, string>();
		string GetShortFileKey(AssemblyFile file)
			=> GetShortFileKey(file.Key);
		string GetShortFileKey(string file)
		{
			if (!_fileKeyMap.ContainsKey(file))
			{
				var idx = file.LastIndexOfAny(new char[] { '/', '\\' }) + 1;
				_fileKeyMap.Add(file, util.BoundedSubstr(file, idx, 6) + "_" + _fileKeyIdx++);
			}
			return JsStr(_fileKeyMap[file]);
		}

		void EmitLoadFunction(StringBuilder sb, AssemblyFile file, AssemblyCode code)
		{
			sb.Append(RUNTIME + "._initFuncs.push");
			using (Parenthesis(sb))
				EmitFunction(sb, file, code);
			sb.Append(";");
		}

		const string SCOPE_VAR = "__DIV";
		const string FUNCS = "__FUNCS";
		const string RUNTIME = "__RUN";
		const string THIS = "__THIS"; // TODO: I can just implement this with .apply()
		const string INJECT = "capture";
		const string ERROR = "error";
		void EmitFunction(StringBuilder sb, AssemblyFile file, AssemblyCode code)
		{
			if (code.IsAsync)
				sb.Append("async ");
			sb.Append("function");
			if (code.DoesYield()) sb.Append("*");
			int lineIdx = 0;
			List<string> pNames = new List<string>();
			string multiFix = "";
			using (Parenthesis(sb))
			{
				sb.Append(SCOPE_VAR);
				for (; lineIdx < code.Count; lineIdx++)
				{
					var name = code[lineIdx].Literal;
					if (code[lineIdx].Command == eAsmCommand.ReadParam)
						sb.Append(", " + GetVarName(name));
					else if (code[lineIdx].Command == eAsmCommand.ReadMultiParam)
					{
						sb.Append(",..." + GetVarName(name));
						multiFix = GetVarName(name) + "=new OObj(null, " + GetVarName(name) + ");";
					}
					else break;
					pNames.Add(name);
				}
			}
			using (Braces(sb))
			{
				if (multiFix != "") sb.AppendLine(multiFix);
				EmitKnownVariables(sb, code, pNames);
				if (code.AllowInjection)
				{
					var combo = new HashSet<string>(code._assumedVariables);
					combo.UnionWith(code._injectableParams);
					foreach (var name in combo)
						sb.AppendLine("if('" + GetVarName(name) + "' in " + SCOPE_VAR + "." + INJECT + "){" + GetVarName(name) + "=" + SCOPE_VAR + "." + INJECT + "[" + JsStr(GetVarName(name)) + "];}");
				}
				EmitCodeRemainder(sb, file, code, lineIdx);
			}
		}
		void EmitCodeRemainder(StringBuilder sb, AssemblyFile file, AssemblyCode code, int firstLine)
			=> EmitCodeRange(sb, file, code, firstLine, code.Count - 1);
		void EmitKnownVariables(StringBuilder sb, AssemblyCode code, List<string> pNames)
		{
			var done = new HashSet<string>(pNames);
			List<string> toWrite = new List<string>();
			foreach (var name in code.LocalVariables)
				if (!done.Contains(name))
					toWrite.Add(GetVarName(name));
			if (toWrite.Count > 0)
				sb.AppendLine("var " + util.Join(toWrite, ",") + ";");
			if (code.LocalVariables.Contains(LambdaPointer.THIS))
				sb.AppendLine(THIS + "=" + SCOPE_VAR + "." + THIS + ";");
		}
		string GetUniqVarName(int line) => "T" + line;

		Tuple<int, int, bool> _StackBacktrack(AssemblyCode code, int line, int count = 1)
		{
			// this is obviously innefficient, but we never should be doing this in real time
			int carry = 0;
			for (int i = line - 1; i >= 0; i--)
			{
				code[i].GetStackManip(out var pop, out var push);
				bool multi = push > 1; // see ReadReturn and IterLoadCurrPacked
				while (push >= 1)
				{
					push--;
					if (carry != 0) { carry++; }
					else
					{
						count--;
						if (count == 0)
							return new Tuple<int, int, bool>(i, push, multi);
					}
				}
				carry -= pop;
			}
			throw new Exception("failed to reassemble stack");
		}

		int StackBacktrackIdx(AssemblyCode code, int line, int count = 1)
		{
			return _StackBacktrack(code, line, count).Item1;
		}
		string StackBacktrack(AssemblyCode code, int line, int count = 1)
		{
			var pair = _StackBacktrack(code, line, count);
			var i = pair.Item1;
			var push = pair.Item2;
			var multi = pair.Item3;
			return GetUniqVarName(i) + (multi ? "[" + push + "]" : "");
		}
		List<string> StackBacktrackMulti(AssemblyCode code, int line, int count = 1, int skip = 0)
		{
			List<string> list = new List<string>();
			for (int i = skip; i < count + skip; i++)
				list.Add(StackBacktrack(code, line, i + 1));
			return list;
		}

		void EmitCodeRange(StringBuilder sb, AssemblyFile file, AssemblyCode code, int firstLine, int lastLine)
		{
			for (var idx = firstLine; idx <= lastLine; idx++)
			{
				var line = code[idx];
				string WrapVal(string val)
					=> "gVal(" + val + ")";
				void Math(string op)
				{
					var a = StackBacktrack(code, idx, 2);
					var b = StackBacktrack(code, idx, 1);
					Push(WrapVal(a) + op + WrapVal(b));
				}
				void MathFunc(string func)
				{
					var a = StackBacktrack(code, idx, 2);
					var b = StackBacktrack(code, idx, 1);
					Push(func + "(" + a + "," + b + ")");
				}
				void Push(string value)
					=> sb.AppendLine("var " + GetUniqVarName(idx) + "=" + value + ";");
				string Pop() => StackBacktrack(code, idx);
				string Pop2() => StackBacktrack(code, idx, 2);
				string PopX(int depth) => StackBacktrack(code, idx, depth);
				string Prev() => GetUniqVarName(idx - 1);

				void DoIf(string condition, string insertBegin = "")
				{
					var next = code.FindNextStackLevelLine(idx, line.AssemblyStackLevel);
					sb.AppendLine("if(" + condition + ")");
					using (Braces(sb))
					{
						if (insertBegin != "") sb.AppendLine(insertBegin);
						EmitCodeRange(sb, file, code, idx + 1, next - 1);
					}
					idx = next - 1;
				}
				eAsmCommand PrevCommand() => idx > 0 ? code[idx - 1].Command : eAsmCommand.NoOp;
				eAsmCommand PrevCommand2() => idx > 1 ? code[idx - 2].Command : eAsmCommand.NoOp;


				bool IsAsync()
				{
					if (PrevCommand() == eAsmCommand.PassParams)
						return (PrevCommand2() == eAsmCommand.BeginAwaitCall);
					return (PrevCommand() == eAsmCommand.BeginAwaitCall);
				}
				string ExpAwait() => IsAsync() ? "await " : "";

				string GenPassParamRaw(string scopePass = "null")
				{
					if (PrevCommand() != eAsmCommand.PassParams)
						return scopePass;
					return scopePass + ",..." + Prev();
				}
				string GenPassParams(string scopePass = "null")
						=> "(" + GenPassParamRaw(scopePass) + ")";
				string GenFuncCall(string lambda, string scopePass = "null")
					=> ExpAwait() + "OObj._RunLambda(" + lambda + "," + GenPassParamRaw(scopePass) + ")";

				string Ref(string str) => "DU.Ref(" + str + ")";

				string GenCapture(IEnumerable<string> cvars)
					=> "{" + util.Join(cvars.Select(p => GetVarName(p) + ": DU.Ref(" + GetVarName(p) + ")"), ",") + "}";
				void ManualCapture(string name, string value)
				{
					sb.AppendLine(Pop() + ".InjectCapture(" + JsStr(GetVarName(name)) + "," + value + ");");
				}
				void ShortCircuit(bool truthy)
				{
					var secondTest = code.FindNextStackLevelLine(idx, line.AssemblyStackLevel);
					sb.AppendLine("if(" + (truthy ? "!!" : "!") + Pop() + ")");
					//  this is pretty hacky - it assumes the last instruction is the output used by
					using (Braces(sb))
						sb.AppendLine("var " + GetUniqVarName(secondTest - 1) + "=" + (truthy ? "true" : "false") + ";");
					sb.Append("else");
					using (Braces(sb))
						EmitCodeRange(sb, file, code, idx + 1, secondTest - 1);
					idx = secondTest - 1;
				}


				if (_symbols != null)
				{
					var buffer = _symbols.FileMap[file.Key];
					var fileLine = line.FileLine;
					if (fileLine >= 0 && (idx == 0 || code[idx - 1].FileLine != fileLine))
					{
						var text = buffer.TextAtLine(fileLine).Trim();
						if (text != "")
							sb.AppendLine("//  " + text);
					}
				}


				switch (line.Command)
				{
					case eAsmCommand.NoOp: break;

					case eAsmCommand.StoreLocal:
					case eAsmCommand.StoreNewLocal:
					case eAsmCommand.ReplaceOrNewLocal:
						sb.AppendLine(GetVarName(line.Literal) + "=" + Pop() + ";");
						break;

					case eAsmCommand.StoreProperty:
						sb.AppendLine("OObj.SetChild(" + Pop() + "," + JsStr(line.Literal) + "," + Pop2() + ");");
						break;
					case eAsmCommand.StoreNewLocalRetain:
						sb.AppendLine("OObj.SetChild(" + Pop() + "," + JsStr(line.Literal) + "," + Pop2() + ");");
						Push(Pop2());
						break;
					case eAsmCommand.Load:
						Push(Ref(GetVarName(line.Literal)));
						break;
					case eAsmCommand.LoadPathData:
						Push(Ref(RUNTIME + ".getStaticGlo(" + JsStr(line.Literal) + ")"));
						break;
					case eAsmCommand.LoadPathFile:
						{
							var path = util.Piece(line.Literal, "|", 1);
							var fk = util.Piece(line.Literal, "|", 2);
							Push(Ref(RUNTIME + ".getStaticFile(" + GetShortFileKey(fk) + "," + JsStr(path) + ")"));
							break;
						}
					case eAsmCommand.LoadScratch:
						Push(Ref(RUNTIME + ".getScratchFile(" + Pop() + "," + GetShortFileKey(line.Literal) + ")"));
						break;
					case eAsmCommand.ShadowLoad:
						Push(GetVarName(line.Literal));
						sb.AppendLine(GetVarName(line.Literal) + "=null;");
						break;
					case eAsmCommand.KeyAssign:
						{
							var list = StackBacktrackMulti(code, idx, line.Param);
							list.Reverse();
							sb.AppendLine("OObj.SetPath(" + PopX(line.Param + 1) + ",[" + util.Join(list, ",") + "]," + PopX(line.Param + 2) + ");");
							break;
						}
					case eAsmCommand.StoreNewScratch:
						sb.AppendLine(RUNTIME + ".setScratchFile(" + Pop() + "," + GetShortFileKey(line.Literal) + "," + Pop2() + ");");
						break;
					case eAsmCommand.FreeScratch:
						Push(RUNTIME + ".getScratchFile(" + Pop() + "," + GetShortFileKey(line.Literal) + ")");
						sb.AppendLine(RUNTIME + ".setScratchFile(" + Pop() + "," + GetShortFileKey(line.Literal) + ",null);");
						break;
					case eAsmCommand.SetFileContext:
						Push(GetShortFileKey(line.Literal));
						break;
					case eAsmCommand.PushInt:
						Push("" + line.Param);
						break;
					case eAsmCommand.PushString:
						Push(JsStr(line.Literal));
						break;
					case eAsmCommand.PushBool:
						Push(line.Param == 1 ? "true" : "false");
						break;
					case eAsmCommand.PushFloat:
						Push("" + line.FloatLiteral);
						break;
					case eAsmCommand.PushNull:
						Push("null");
						break;
					case eAsmCommand.DoIfTest:
						{
							var exit = code.FindNextStackLevelLine(idx, line.AssemblyStackLevel - 1);
							var elseCond = code.FindNextStackLevelLine(idx, line.AssemblyStackLevel);
							sb.AppendLine("if(" + Pop() + ")");
							var hasElse = (exit != elseCond);
							using (Braces(sb))
								EmitCodeRange(sb, file, code, idx + 1, elseCond - 1);
							if (hasElse)
							{
								sb.AppendLine("else");
								using (Braces(sb))
									EmitCodeRange(sb, file, code, elseCond, exit - 1);
							}
							idx = exit - 1;
							break;
						}
					case eAsmCommand.ShortCircuitTrue:
						ShortCircuit(true);
						break;
					case eAsmCommand.ShortCircuitFalse:
						ShortCircuit(false);
						break;
					case eAsmCommand.ShortCircuitNotNull:
						{
							var secondTest = code.FindNextStackLevelLine(idx, line.AssemblyStackLevel);
							sb.AppendLine("if(" + Pop() + " != null)");
							//  this is pretty hacky - it assumes the last instruction is the output used
							using (Braces(sb))
								sb.AppendLine("var " + GetUniqVarName(secondTest - 1) + "=" + Pop() + ";");
							sb.Append("else");
							using (Braces(sb))
								EmitCodeRange(sb, file, code, idx + 1, secondTest - 1);
							idx = secondTest - 1;
							break;
						}
					case eAsmCommand.ShortCircuitPropNull:
						DoIf("OObj.HasChildKey(" + Pop() + "," + JsStr(line.Literal) + ")", "var " + GetUniqVarName(idx) + "=" + Pop() + ";");
						sb.AppendLine("else {var " + GetUniqVarName(idx) + "=null;}");
						break;
					case eAsmCommand.ShortCircuitNull:
						DoIf(Pop() + " != null", "var " + GetUniqVarName(idx) + "=" + Pop() + ";");
						break;
					case eAsmCommand.Equals:
						MathFunc("DU.AreEqual");
						break;
					case eAsmCommand.NotEquals:
						MathFunc("!DU.AreEqual");
						break;
					case eAsmCommand.HasValue:
						Push(Pop() + " != null");
						break;
					case eAsmCommand.IsNull:
						Push(Pop() + " == null");
						break;
					case eAsmCommand.GreaterThan:
						Math(" > ");
						break;
					case eAsmCommand.LessThan:
						Math(" < ");
						break;
					case eAsmCommand.EqGreater:
						Math(" >= ");
						break;
					case eAsmCommand.EqLess:
						Math(" <= ");
						break;
					case eAsmCommand.ExceedInDirection:
						{
							sb.Append("if(" + GetVarName(line.Literal) + ">0)");
							using (Braces(sb))
								Math(" > ");
							sb.AppendLine("else");
							using (Braces(sb))
								Math(" < ");
							break;
						}
					case eAsmCommand.And:
						Math(" && ");
						break;
					case eAsmCommand.Or:
						Math(" || ");
						break;
					case eAsmCommand.NullCoalesce:
						Math(" ?? ");
						break;
					case eAsmCommand.Pop: break;
					case eAsmCommand.Not:
						Push("!" + StackBacktrack(code, idx, 1));
						break;
					case eAsmCommand.Meh:
						Push(Pop());
						break;
					case eAsmCommand.Return:
						{
							var list = StackBacktrackMulti(code, idx, line.Param);
							list.Reverse();
							sb.Append("return [" + util.Join(list, ",") + "];");
							break;
						}
					case eAsmCommand.ReadReturn:
						Push("DU.ReadReturn(" + Prev() + "," + line.Param + ")");
						break;
					case eAsmCommand.Quit:
						{
							var list = code.GetReturnIdxs();
							var names = list.Select(i => GetVarName(code.LocalVariables[i]));
							sb.AppendLine("return [" + util.Join(names, ",") + "];");
							break;
						}
					case eAsmCommand.Jump: break;
					case eAsmCommand.Break:
						sb.AppendLine("break;");
						break;
					case eAsmCommand.Continue:
						sb.AppendLine("continue;");
						break;
					case eAsmCommand.Add:
						Math("+");
						break;
					case eAsmCommand.Subtract:
						Math("-");
						break;
					case eAsmCommand.Multiply:
						Math("*");
						break;
					case eAsmCommand.Divide:
						// CAUTION: division behaves differently between js and bytecode! :(
						// I prefer the way it works in bytecode, but js auto turns 200.0 into 200
						// so if I try to detect "oh you want integer division" it will sometimes fail
						Math("/");
						break;
					case eAsmCommand.ClearRegisters: break;
					case eAsmCommand.DeclareFunction:
						{
							var fk = GetShortFileKey(code.FileKey);
							sb.AppendLine(RUNTIME + ".setStaticFile(" + fk + "," + Pop() + ",OObj.MakeFunc(" + FUNCS + "[" + fk + "][" + JsStr(line.Literal) + "]));");
						}
						break;
					case eAsmCommand.StoreToPathData:
						sb.AppendLine(RUNTIME + ".setStaticGlo(" + JsStr(line.Literal) + "," + Pop() + ");");
						break;
					case eAsmCommand.StoreToData: break; // might want to do this? I think it's a waste of time
					case eAsmCommand.StoreToFileConst:
						sb.AppendLine(RUNTIME + ".setStaticFile(" + GetShortFileKey(code.FileKey) + "," + JsStr(line.Literal) + "," + Pop() + ");");
						break;
					case eAsmCommand.StoreEnumToPathData:
						sb.AppendLine(RUNTIME + ".setStaticGloEnum(" + JsStr(line.Literal) + "," + Pop() + ");");
						break;
					case eAsmCommand.StoreEnumToFileConst:
						sb.AppendLine(RUNTIME + ".setStaticFileEnum(" + GetShortFileKey(code.FileKey) + "," + JsStr(line.Literal) + "," + Pop() + ");");
						break;
					case eAsmCommand.BeginAwaitCall: break; // handled in calling functions
					case eAsmCommand.ARunCode:
						{
							var func = Pop();
							Push(RUNTIME + ".runJobLambda(" + func + ")");
							break;
						}
					case eAsmCommand.PassParams:
						{
							var list = StackBacktrackMulti(code, idx, line.Param);
							list.Reverse();
							Push("[" + util.Join(list, ",") + "]");
							break;
						}
					case eAsmCommand.IgnoreParams: break;
					case eAsmCommand.CallFunc:
						Push(ExpAwait() + GetVarName(line.Literal) + ".func" + GenPassParams());
						break;
					case eAsmCommand.CallMethod:
						Push(ExpAwait() + Pop() + ".inner." + line.Literal + ".func" + GenPassParams("{" + THIS + ":" + Pop() + "}"));
						break;
					case eAsmCommand.ExecNamed:
						{
							var func = Pop();
							var toPass = new List<string>();
							for (int i = 0; i < line.Param; i++)
							{
								var tx = StackBacktrackIdx(code, idx, i + 2);
								toPass.Add(code[tx].Literal);
							}
							var capture = "{" + util.Join(toPass.Select(p => GetVarName(p) + ": DU.Ref(" + GetVarName(p) + ")"), ",") + "}";
							Push(GenFuncCall(func, capture));
							break;
						}
					case eAsmCommand.CallFileFunc:
						{
							var arr = StaticMapping.SplitAbsPath(line.Literal, out _, out var fk);
							var path = StaticMapping.JoinPath(arr);
							Push(ExpAwait() + RUNTIME + ".getStaticFile(" + GetShortFileKey(fk) + "," + JsStr(path) + ").func" + GenPassParams());
							break;
						}
					case eAsmCommand.CallPathFunc:
						{
							var path = StaticMapping.GetPathFromAbsPath(line.Literal);
							Push(ExpAwait() + RUNTIME + ".getStaticGlo(" + JsStr(path) + ").func" + GenPassParams());
							break;
						}
					case eAsmCommand.CallFileMethod:
						{
							var arr = StaticMapping.SplitAbsPath(line.Literal, out _, out var fk);
							var path = StaticMapping.JoinPath(arr);
							var thisVar = Pop();
							Push(ExpAwait() + RUNTIME + ".getStaticFile(" + GetShortFileKey(fk) + "," + JsStr(path) + ").func" + GenPassParams("{" + THIS + ":" + thisVar + "}"));
							break;
						}
					case eAsmCommand.CallPathMethod:
						{
							var path = StaticMapping.GetPathFromAbsPath(line.Literal);
							var thisVar = Pop();
							Push(ExpAwait() + RUNTIME + ".getStaticGlo(" + JsStr(path) + ").func" + GenPassParams("{" + THIS + ":" + thisVar + "}"));
							break;
						}
					case eAsmCommand.DotAccess:
						Push("OObj.DotAccess(" + Pop() + "," + JsStr(line.Literal) + ")");
						break;
					case eAsmCommand.Has:
						Push(Pop() + ".inner!=null && (" + JsStr(line.Literal) + " in " + Pop() + ".inner)");
						break;
					case eAsmCommand.LoadFirstKey:
						Push("OObj.getFirstKey(" + Pop() + ")");
						break;
					case eAsmCommand.LoadNextKey:
						Push("OObj.getNextKey(" + Pop2() + "," + Pop() + ")");
						break;
					case eAsmCommand.LoadLastKey:
						Push("OObj.getLastKey(" + Pop() + ")");
						break;
					case eAsmCommand.Copy:
						Push("DU.Copy(" + Pop() + ")");
						break;
					case eAsmCommand.KeyAccess:
						{
							var list = StackBacktrackMulti(code, idx, line.Param);
							list.Reverse();
							Push("OObj.GetPath(" + PopX(line.Param + 1) + ",[" + util.Join(list, ",") + "])");
							break;
						}
					case eAsmCommand.FreeProperty:
						Push("OObj.FreePopChild(" + Pop() + "," + JsStr(line.Literal) + ")");
						break;
					case eAsmCommand.FreeLocal:
						// this allows some stuff it shouldn't...
						Push(GetVarName(line.Literal));
						sb.AppendLine(GetVarName(line.Literal) + "=undefined;");
						break;
					case eAsmCommand.SoftFreeLocal:
						Push(GetVarName(line.Literal));
						sb.AppendLine(GetVarName(line.Literal) + "=undefined;");
						break;
					case eAsmCommand.SoftFreeKey:
					case eAsmCommand.KeyFree:
						{
							var list = StackBacktrackMulti(code, idx, line.Param);
							list.Reverse();
							Push("OObj.FreePath(" + PopX(line.Param + 1) + ", [" + util.Join(list, ",") + "])");
							break;
						}
					case eAsmCommand.CreateStaticFuncPointer:
						Push(RUNTIME + ".getStaticGlo(" + JsStr(line.Literal) + ")");
						break;
					case eAsmCommand.CreateLambda:
						{
							var lCode = file[line.Literal];
							var source = new HashSet<string>(code.LocalVariables);
							var target = new HashSet<string>(lCode.LocalVariables);
							source.IntersectWith(target);
							var capture = GenCapture(source);
							var fk = GetShortFileKey(code.FileKey);
							Push("OObj.MakeFunc(" + FUNCS + "[" + fk + "][" + JsStr(line.Literal) + "]," + capture + ")");
							break;
						}
					case eAsmCommand.CreateManualLambda:
						{
							var fk = GetShortFileKey(code.FileKey);
							Push("OObj.MakeFunc(" + FUNCS + "[" + fk + "][" + JsStr(line.Literal) + "],{})");
							break;
						}
					case eAsmCommand.CaptureCopy:
						{
							ManualCapture(line.Literal, "DU.Copy(" + GetVarName(line.Literal) + ")");
							break;
						}
					case eAsmCommand.CaptureFree:
					case eAsmCommand.CaptureFreeish:
						{
							ManualCapture(line.Literal, GetVarName(line.Literal));
							sb.AppendLine(GetVarName(line.Literal) + "=undefined;");
							break;
						}
					case eAsmCommand.CaptureVar:
						{
							ManualCapture(line.Literal, Ref(GetVarName(line.Literal)));
							break;
						}
					case eAsmCommand.DeclareStaticFunction:
						{
							var fk = GetShortFileKey(code.FileKey);
							Push("OObj.MakeFunc(" + FUNCS + "[" + fk + "][" + JsStr(line.Literal) + "])");
							break;
						}
					case eAsmCommand.ReserveLocal:
						sb.AppendLine(GetVarName(line.Literal) + "=undefined;");
						break;
					case eAsmCommand.ReserveScratch:
						sb.AppendLine(RUNTIME + ".setScratchFile(" + Pop() + "," + GetShortFileKey(line.Literal) + ",null);");
						break;
					case eAsmCommand.DimArray:
						Push("new OObj()");
						break;
					case eAsmCommand.DimDictionary:
						Push("new OObj()");
						break;
					case eAsmCommand.DimSetInt:
						sb.AppendLine("OObj.SetChild(" + Pop2() + "," + line.Param + "," + Pop() + ");");
						break;
					case eAsmCommand.DimSetString:
						sb.AppendLine("OObj.SetChild(" + Pop2() + "," + JsStr(line.Literal) + "," + Pop() + ");");
						break;
					case eAsmCommand.DimSetExpr:
						sb.AppendLine("OObj.SetChild(" + PopX(3) + "," + Pop() + "," + Pop2() + ");");
						break;
					case eAsmCommand.SetupMixin:
						// this command doesn't work the way it sounds -- see instruction set
						Push(Ref(Pop()));
						break;
					case eAsmCommand.PreYield: break;
					case eAsmCommand.YieldIterator:
						{
							var list = StackBacktrackMulti(code, idx, line.Param);
							sb.AppendLine("yield [" + util.Join(list, ",") + " ];");
							break;
						}
					case eAsmCommand.YieldFinalize: break;
					case eAsmCommand.IterCreate:
						Push("DU.MakeIter(" + Pop() + ")");
						break;
					case eAsmCommand.IterIsComplete:
						Push(Pop() + ".iter.done");
						break;
					case eAsmCommand.IterMoveNext:
						sb.AppendLine(Pop() + ".Next()" + ";");
						break;
					case eAsmCommand.IterLoadCurrent:
						Push(Pop() + ".iter.value[0]");
						break;
					case eAsmCommand.IterLoadCurrPacked:
						Push(Pop() + ".iter.value");
						break;
					case eAsmCommand.LoopBegin:
						{
							var exit = code.FindNextStackLevelLine(idx, line.AssemblyStackLevel);
							var iter = GetUniqVarName(idx);
							sb.AppendLine("for(var " + iter + "=0;;" + iter + "++)");
							using (Braces(sb))
							{
								var inner = code.FindSkipIntoDepth(idx, line.Param);
								if (inner > idx + 1)
								{
									sb.AppendLine("if(" + iter + ">0)");
									using (Braces(sb))
										EmitCodeRange(sb, file, code, idx + 1, inner - 1);
								}
								EmitCodeRange(sb, file, code, inner, exit - 1);
							}
							idx = exit - 1;
							break;
						}
					case eAsmCommand.LoopIfTest:
						sb.AppendLine("if(!" + Pop() + "){break;}");
						break;
					case eAsmCommand.TestIfUninitialized:
						Push(GetVarName(line.Literal) + "==null");
						break;
					case eAsmCommand.AssertOwnedVar:
						sb.AppendLine("DU.AssertOwned(" + GetVarName(line.Literal) + ");");
						break;
					case eAsmCommand.CreateErrorTrap:
						{
							var exit = code.FindNextStackLevelLine(idx, line.AssemblyStackLevel);
							var trap = code.FindNextStackLevelLine(idx, line.Param);
							sb.AppendLine("try");
							using (Braces(sb))
								EmitCodeRange(sb, file, code, idx + 1, trap - 1);
							sb.AppendLine("catch(" + GetVarName(ERROR) + ")");
							using (Braces(sb))
								EmitCodeRange(sb, file, code, trap, exit - 1);
							idx = exit - 1;
							break;
						}
					case eAsmCommand.FatalError:
						sb.AppendLine("fatalError();");
						break;
					case eAsmCommand.ThrowError:
						sb.AppendLine("throw " + (line.Param == 1 ? Pop() : "null") + ";");
						break;
					case eAsmCommand.ClearErrorTrap: break;
					case eAsmCommand.FlagDefer:
						sb.AppendLine("if(" + GetVarName(line.Literal) + "){throw null;}");
						sb.AppendLine(GetVarName(line.Literal) + "=true;");
						break;
					case eAsmCommand.RunDeferIfSet:
						{
							var exit = code.FindNextStackLevelLine(idx, line.AssemblyStackLevel);
							sb.AppendLine("if(" + GetVarName(line.Literal) + ")");
							using (Braces(sb))
								EmitCodeRange(sb, file, code, idx + 1, exit - 1);
							idx = exit - 1;
							break;
						}
					default: throw new NotImplementedException();
				}
			}
		}

		string GetVarName(string literal)
		{
			if (literal == LambdaPointer.THIS) return THIS;
			return "$" + literal.Replace("%", "$").Replace("*", "_");
		}



		#region helpers
		string EscapeStr(string str) => str.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
		string JsStr(string str) => "\"" + EscapeStr(str) + "\"";
		PairedInserter Braces(StringBuilder sb) => new PairedInserter(sb, "{\n", "}");
		PairedInserter Parenthesis(StringBuilder sb) => new PairedInserter(sb, "(", ")");
		class PairedInserter : IDisposable
		{
			StringBuilder _sb;
			string _end;
			public PairedInserter(StringBuilder sb, string start, string end)
			{
				_sb = sb;
				_end = end;
				sb.Append(start);
			}

			public void Dispose()
			{
				_sb.Append(_end);
			}
		}
		public static string AppendPiece(string start, string splitter, string append)
		{
			if (start == "") return append;
			return start + splitter + append;
		}

		#endregion
	}
}
