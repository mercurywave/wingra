using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	public class Compiler
	{
		internal bool _isDebug, _isTest, _isSuggestion, _isIDE, _isBootstrap, _isAsmDebug;
		internal bool _hideExternalFuncs;
		internal bool _alwaysTypeCheckParams;
		public bool Optimizations = true;
		public bool SanityChecks => _isDebug || _isIDE || _isTest; // additional compile-time checks
		Dictionary<string, Tuple<string, CodeBlock>> _macros = new Dictionary<string, Tuple<string, CodeBlock>>();
		ORuntime _macroRuntime;
		Compiler _macroCompiler;
		List<WingraBuffer> _bootstrap = new List<WingraBuffer>();
		public StaticMapping StaticMap; // written in parse, read during emit
		internal int InlineDepth = 0;
		public Compiler(StaticMapping mapping, bool isDebug, bool isTest, bool isSuggestion, bool isIDE, bool isAsmDebug = false)
		{
			StaticMap = mapping;
			_isDebug = isDebug;
			_isTest = isTest;
			_isSuggestion = isSuggestion;
			_isIDE = isIDE;
			_isAsmDebug = isAsmDebug;
			_ResetMacroRuntime();
		}
		// primarily for building the static map prior to export, where symbols won't be resolved during the compile
		public Compiler(WingraProject proj, StaticMapping mapping, bool isSuggestion) : this(mapping, false, proj.DoRunTests, isSuggestion, false)
		{
			if (proj.CheckConfigFlag("disableOptimizations"))
				Optimizations = false;
			if (proj.CheckConfigFlag("hideExternalFuncs") || proj.IsJsExport)
				_hideExternalFuncs = true;
			if (proj.CheckConfigFlag("typeCheckParams"))
				_alwaysTypeCheckParams = true;
		}
		public Compiler(WingraProject proj, StaticMapping mapping) : this(proj, mapping, false) { }
		public Compiler(WingraProject proj) : this(proj, new StaticMapping()) { }
		Compiler(StaticMapping mapping, Compiler parent)
		{
			// this should only be used for the macro compiler
			StaticMap = mapping;
			_isDebug = parent._isDebug;
			_isTest = parent._isTest;
			_isSuggestion = parent._isSuggestion;
			_isIDE = parent._isIDE;
		}

		void _ResetMacroRuntime()
		{
			InlineDepth = 0;
			_macroRuntime = new ORuntime() { Debug = _isDebug, StaticMap = this.StaticMap };
			_macroCompiler = new Compiler(this.StaticMap, this);
			LCompiler.Setup(_macroRuntime, this);
		}

		// for now, all macros must be independant, and can't use other macros
		// I could potentially compile all macros into a single file, but that sounds complicated
		public void PreParse(WingraBuffer buffer, ErrorLogger errors)
		{
			_lastBuffer = buffer;

			if (buffer.Lines > 0)
			{
				var lex = buffer.GetSyntaxMetadata(0);
				if (!lex.IsEmpty && lex.Tokens[0].Type == eToken.BootStrap && !_bootstrap.Contains(buffer))
					_bootstrap.Add(buffer);
			}
			for (int buffLine = 0; buffLine < buffer.Lines; buffLine++)
			{
				_buffLine = buffLine;
				var lex = buffer.GetSyntaxMetadata(buffLine);
				if (lex.DefinesMacro && lex.Tokens.Count > 1)
				{
					var name = lex.Tokens[1].Token; // assumes first token is always "#def" from DefinesMacro
					var indent = lex.PreceedingWhitespace;
					WingraBuffer fake = new WingraBuffer(name);
					for (int j = buffLine + 1; j < buffer.Lines; j++)
					{
						var next = buffer.GetSyntaxMetadata(j);
						if (next.PreceedingWhitespace <= indent && !next.IsEmpty) break;
						fake.AppendLine(buffer.TextAtLine(j)); // there's a tiny amount of double work here
					}
					SMacroDef def = new SMacroDef(buffLine, name);
					_macroCompiler._Parse(fake, errors, def);

					FileAssembler ass = new FileAssembler("", "");
					FunctionFactory func = new FunctionFactory();
					func.ReserveVariable("code");
					func.ReserveVariable("meta");
					def.EmitAssembly(_macroCompiler, ass, func, 0, errors, null); // passing null could be a problem here
					_macros[name] = new Tuple<string, CodeBlock>(buffer.Key, new CodeBlock(func.AssembleExpression()));
					buffLine += fake.Lines;
				}
			}
			bool leadingTabs = false;
			bool leadingSpaces = false;
			string GetLead(string lineText)
			{
				for (int i = 0; i < lineText.Length; i++)
				{
					if (lineText[i] != ' ' && lineText[i] != '\t')
						return util.BoundedSubstr(lineText, 0, i);
				}
				return "";
			}
			for (int buffLine = 0; buffLine < buffer.Lines; buffLine++)
			{
				var lead = GetLead(buffer.TextAtLine(buffLine));
				if (!leadingTabs && lead.Contains('\t'))
					leadingTabs = true;
				if (!leadingSpaces && lead.Contains(' '))
					leadingSpaces = true;
				if (leadingSpaces && leadingTabs)
				{
					errors.LogError("Mix of leading tabs and spaces - parsing might behave erratically", ePhase.PreParse, buffLine, null, eErrorType.Warning);
					break;
				}
			}
		}

		public void Bootstrap(ErrorLogger errors)
		{
			if (_macroRuntime._initialized) _ResetMacroRuntime();
			if (errors.AnyLogged) return;
			_isBootstrap = true;
			foreach (var buffer in _bootstrap)
			{
				var parse = Parse(buffer, errors);
				if (errors.AnyLogged) return;

				var assm = Compile(buffer.ShortFileName, buffer.Key, parse, errors);
				if (errors.AnyLogged) return;

				_macroRuntime.RegisterFiles(assm);
			}
			_macroRuntime.Initialize(this);
			_isBootstrap = false;
		}

		public STopOfFile Parse(WingraBuffer buffer, ErrorLogger errors, FileScopeTracker tracker = null)
		{
			var topLevel = new STopOfFile();
			_Parse(buffer, errors, topLevel, tracker);
			return topLevel;
		}

		// these exist to allow macros to peek into the state
		// this means the compiler isn't threadsafe!
		internal int _buffLine = -1;
		internal WingraBuffer _lastBuffer;
		internal int FileLine => _buffLine;
		void _Parse(WingraBuffer buffer, ErrorLogger errors, IHaveChildScope topLevel, FileScopeTracker tracker = null)
		{
			_lastBuffer = buffer;
			var scope = new ScopeStack(topLevel);
			var expandedLine = buffer.Lines;

			for (int buffLine = 0; buffLine < buffer.Lines;)
			{
				_buffLine = buffLine;
				var lexline = buffer.GetSyntaxMetadata(buffLine);
				var text = buffer.TextAtLine(buffLine);
				if (lexline.DefinesMacro)
				{
					// need to skip over all lines inside the macro def
					// must have been handled by pre-parse
					var indent = lexline.PreceedingWhitespace;
					int j = 1;
					for (; j + buffLine < buffer.Lines; j++)
					{
						var nextLine = buffer.GetSyntaxMetadata(buffLine + j);
						if (nextLine.PreceedingWhitespace <= indent && !nextLine.IsEmpty) break;
					}
					buffLine += j;
					continue;
				}

				if (!_ParseAhead(buffer, errors, topLevel, scope, ref buffLine, text, lexline, ref expandedLine, tracker))
					return;
			}
		}

		bool _ParseAhead(WingraBuffer buffer, ErrorLogger errors, IHaveChildScope topLevel, ScopeStack scope, ref int buffLine, string lineText, LexLine lexline, ref int expandedLine, FileScopeTracker tracker = null)
		{
			if (lexline.ContainsMacro)
			{
				if (!ProcessMacro(buffer, errors, buffLine, lineText, lexline, out var replaceLineCount, out var newCode))
					return false;

				List<LexLine> lexed = new List<LexLine>();
				foreach (var padded in newCode)
					lexed.Add(new LexLine(padded, WingraBuffer.SpacesToIndent));

				var offset = buffLine;
				if (lexed.Count > replaceLineCount)
				{
					offset = expandedLine;
					expandedLine += replaceLineCount + 1;
				}

				for (int k = 0; k < lexed.Count; k++)
				{
					if (lexed[k].IsEmpty) continue;
					List<RelativeTokenReference> lineTokes = lexed[k].GetRealRelativeTokens(0);
					int m = 1;
					for (; m + k < lexed.Count; m++)
					{
						var lex = lexed[k + m];
						if (!lex.LineIsContinuation) break;
						lineTokes.AddRange(lex.GetRealRelativeTokens(m));
					}
					var context = new ParseContext(this, buffer, offset + k, errors, scope);
					ParseSingleLine(context, lineTokes, lexed[k].PreceedingWhitespace, tracker);
				}

				buffLine += replaceLineCount;
			}
			else if (!lexline.IsEmpty)
			{
				List<RelativeTokenReference> lineTokes = lexline.GetRealRelativeTokens(0);
				int j = 1;
				if (!(scope.Peek() is STextData))
					for (; j + buffLine < buffer.Lines; j++)
					{
						//This code is duplicative of some of the existing buffer features...
						var lex = buffer.GetSyntaxMetadata(buffLine + j);
						if (!lex.LineIsContinuation) break;
						lineTokes.AddRange(lex.GetRealRelativeTokens(j));
					}

				var context = new ParseContext(this, buffer, buffLine, errors, scope);
				ParseSingleLine(context, lineTokes, lexline.PreceedingWhitespace, tracker);
				buffLine += j;
			}
			else
			{
				scope.Peek().AddBlankLine();
				buffLine++; // empty line
			}
			return true;
		}


		bool ProcessMacro(WingraBuffer buffer, ErrorLogger errors, int buffLine, string lineText, LexLine lexline, out int replacedLineCount, out List<string> newCode)
		{
			int index = lexline.Tokens.FindIndex(t => t.Type == eToken.Macro);
			var ident = lexline.Tokens[index];
			newCode = null;
			if (!_macros.ContainsKey(ident.Token))
			{
				replacedLineCount = 0;
				errors.LogError("Macro " + ident + " not found", ePhase.Macros, buffLine, new RelativeTokenReference(ident, 0));
				return false;
			}
			var preceeding = lineText.Substring(0, lexline.Tokens[index].LineOffset);
			var indent = lexline.PreceedingWhitespace;
			var code = new List<string>();
			code.Add(util.BoundedSubstr(lineText, ident.LineOffset + ident.Length, lineText.Length)); // slightly lazy
			int j = 1;
			for (; j + buffLine < buffer.Lines; j++)
			{
				var nextLine = buffer.GetSyntaxMetadata(buffLine + j);
				if (nextLine.PreceedingWhitespace <= indent && !nextLine.IsEmpty) break;
				var inner = LexLine.RemovePreceedingWhitespace(buffer.TextAtLine(buffLine + j), indent, WingraBuffer.SpacesToIndent);
				code.Add(inner);
			}
			replacedLineCount = j;
			var exec = _macros[ident.Token].Item2;
			var jb = new Job(_macroRuntime, exec);
			List<Variable> vCode = code.Select(c => new Variable(c)).ToList();
			Dictionary<string, Variable> vMeta = new Dictionary<string, Variable>() {
					{"File",  jb.MakeVariable(buffer.ShortFileName) },
					{"Line",  jb.MakeVariable(buffLine) },
				};

			jb.InjectLocal("code", jb.MakeVariable(vCode));
			jb.InjectLocal("meta", jb.MakeVariable(vMeta));
			var output = jb.RunGetReturn();
			if (!output.IsStructLike)
			{
				errors.LogError("Macro " + ident + " did not generate valid code", ePhase.Macros, buffLine, new RelativeTokenReference(ident, 0));
				return false;
			}
			var outCode = output.AsList();
			jb.CheckIn(output);

			newCode = new List<string>();
			int idx = 0;
			foreach (var expand in outCode)
			{
				if (!expand.IsString)
				{
					errors.LogError("Macro " + ident + " did not generate valid list of strings", ePhase.Macros, buffLine, new RelativeTokenReference(ident, 0));
					return false;
				}
				string padded = preceeding + expand.AsString();
				newCode.Add(padded);
				idx++;
			}
			return true;
		}

		public WingraBuffer GenMacroExpansion(WingraBuffer buffer, ErrorLogger errors, WingraBuffer output = null)
		{
			if (output == null)
				output = new WingraBuffer("EXPANDED");
			else
				output.Clear();

			for (int buffLine = 0; buffLine < buffer.Lines; buffLine++)
			{
				_buffLine = buffLine;
				var lexline = buffer.GetSyntaxMetadata(buffLine);
				if (lexline.ContainsMacro)
				{
					if (ProcessMacro(buffer, errors, buffLine, buffer.TextAtLine(buffLine), lexline, out var replacedLines, out var newCode))
					{
						buffLine += replacedLines - 1;
						foreach (var code in newCode)
							output.AppendLine(code);
						continue;
					}
				}
				output.AppendLine(buffer.TextAtLine(buffLine));
			}
			return output;
		}

		static void AddNodeToTree(SyntaxNode node, ParseContext context)
		{
			// this is kinda dumb. I need this step to set things up prior to emit
			// this is a good argument for building like an actual AST I can traverse
			node.OnAddedToTree(context);
		}

		public AssemblyCode CompileStatement(string code)
		{
			var lex = new LexLine(code, 4);
			var err = new MinimalErrorLogger();
			var file = new SFakeFile();
			var context = new ParseContext(this, new WingraBuffer(code), 1, err, new ScopeStack(file));
			ParseSingleLine(context, lex.GetRealRelativeTokens(0), 0);
			var ass = Compile(file, err);
			if (err.EncounteredError) throw new Exception("string could not be compiled");
			return ass;
		}

		public AssemblyCode CompileExpression(string code)
		{
			var lex = new LexLine(code, 4);
			var err = new MinimalErrorLogger();
			var context = new ParseContext(this, new WingraBuffer(code), 1, err, new ScopeStack(null));
			if (!ExpressionParser.TryParseExpression(context, lex.GetRealRelativeTokens(0).ToArray(), out var exp, out int usedTokens))
				throw new Exception("expression could not be parsed");
			AddNodeToTree(exp, context);
			var ass = Compile(exp, err);
			if (err.EncounteredError) throw new Exception("string could not be compiled");
			return ass;
		}

		// basically just compiles an expression that doesn't check locals
		public AssemblyCode CompileLambda(string code)
		{
			var lex = new LexLine(code, 4);
			var err = new MinimalErrorLogger();
			var context = new ParseContext(this, new WingraBuffer(code), 1, err, new ScopeStack());
			if (!ExpressionParser.TryParseExpression(context, lex.GetRealRelativeTokens(0).ToArray(), out var exp, out int usedTokens))
				throw new Exception("expression could not be parsed");
			AddNodeToTree(exp, context);
			var ass = CompileLambda(exp, err);
			if (err.EncounteredError) throw new Exception("string could not be compiled");
			return ass;
		}

		private static void ParseSingleLine(ParseContext context, List<RelativeTokenReference> lineTokes, int preceedingSpaces, FileScopeTracker tracker = null)
		{
			try
			{
				var scope = context.Scope;
				var currIndent = scope.GetUpdateIndent(preceedingSpaces);
				scope.ResetForLine(currIndent);
				int initIndent = currIndent;

				if (tracker != null)
				{
					tracker.ResetLine(context.FileLine);
					foreach (var path in context.Scope.GetUsingNamespaces())
						tracker.AddUsing(context.FileLine, path);
				}

				while (lineTokes.Count > 0)
				{
					var parent = scope.Peek();
					if (parent.TryParseChild(context, lineTokes.ToArray(), out var node, out var used))
					{
						parent.AddChild(node);
						IHaveChildScope childScope = null;
						if (node is IHaveChildScope) childScope = node as IHaveChildScope;
						else if (node is SStatement) childScope = FindChildScopeInverter(node as SStatement);
						AddNodeToTree(node, context);

						if (tracker != null)
						{
							foreach (var path in context.Scope.GetUsingNamespaces())
								tracker.AddUsing(context.FileLine, path);
						}

						if (childScope != null)
							scope.Push(currIndent, childScope);
						if (used == 0) break; // this is probably an internal error, right?
						if (used == lineTokes.Count) break;
						var next = lineTokes[used];
						if (next.Token.Type == eToken.BackSlash)
							currIndent++;
						else if (next.Token.Type != eToken.SemiColon)
						{
							context.LogError("unexpected token: " + next.Token.Token, next);
							break;
						}
						used++;
						scope.ResetForLine(currIndent);
						lineTokes = lineTokes.GetRange(used, lineTokes.Count - used);
					}
					else
					{
						context.LogError("failed to parse statement"); // maybe duplicative...
						break;
					}
				}
				// if there's a colon on the line, the next line should carry forward initial indent scope
				if (currIndent > initIndent) scope.Pop(initIndent);

			}
			catch (ParserException ex)
			{
				context.Errors.LogError(ex.Message, ePhase.Parse, context.FileLine, ex.Token, ex.Type, ex.StackTrace);
			}
		}

		public AssemblyFile Compile(string name, string key, STopOfFile file, ErrorLogger errors)
		{
			try
			{
				FileAssembler ass = new FileAssembler(name, key);
				file.EmitAssembly(this, ass, ass.InitRoutine, 0, errors);
				return ass.Assemble(this, errors);
			}
			catch (CompilerException ce)
			{
				errors.LogError(ce.Message, ePhase.Compile, ce.Line, ce.Token, ce.Type);
			}
			return new AssemblyFile("err", "key");
		}

		// for queries / REPL mostly
		internal AssemblyCode Compile(SFakeFile file, ErrorLogger errors)
		{
			FileAssembler ass = new FileAssembler("", "");
			FunctionFactory func = new FunctionFactory();
			func.AllowUndefined = true;
			file.EmitAssembly(this, ass, func, 0, errors, null); // passing null could be a problem here
			func.InjectDeferrals(0);
			func.Add(0, eAsmCommand.Return);
			return func.AssembleExpression();
		}

		// for queries / REPL mostly
		internal AssemblyCode Compile(SExpressionComponent exp, ErrorLogger errors)
		{
			FileAssembler ass = new FileAssembler("", "");
			FunctionFactory func = new FunctionFactory();
			func.AllowUndefined = true;
			exp.EmitAssembly(this, ass, func, 0, errors, null); // passing null could be a problem here
			return func.AssembleExpression();
		}
		internal AssemblyCode CompileLambda(SExpressionComponent exp, ErrorLogger errors)
		{
			FileAssembler ass = new FileAssembler("", "");
			FunctionFactory func = ass.GenLambda(-1);
			exp.EmitAssembly(this, ass, func, 0, errors, null); // passing null could be a problem here
			func.InjectDeferrals(0);
			func.Add(0, eAsmCommand.Return, 1);
			return func.AssembleExpression();
		}
		internal class ScopeStack
		{
			FastStack<Tuple<int, IHaveChildScope>> _stack = new FastStack<Tuple<int, IHaveChildScope>>();
			FastStack<Tuple<int, string>> _usingScope = new FastStack<Tuple<int, string>>();
			FastStack<Tuple<int, string>> _declaringScope = new FastStack<Tuple<int, string>>();
			FastStack<int> _indents = new FastStack<int>();
			public ScopeStack(IHaveChildScope file) { Push(-1, file); }
			public ScopeStack() : this(new SFakeFile()) { }
			public int GetUpdateIndent(int preceedingSpaces)
			{
				while (!_indents.IsEmpty && _indents.Peek() > preceedingSpaces)
					_indents.Pop();
				if (_indents.IsEmpty && preceedingSpaces > 0)
					_indents.Push(preceedingSpaces);
				else if (!_indents.IsEmpty && preceedingSpaces > _indents.Peek())
					_indents.Push(preceedingSpaces);
				return _indents.Depth;
			}
			public void ResetForLine(int indent)
			{
				// shouldn't need error handling because the file scope sits at the top
				while (_stack.Peek().Item1 >= indent)
					_stack.Pop();
				while (!_usingScope.IsEmpty && _usingScope.Peek().Item1 >= indent)
					_usingScope.Pop();
				while (!_declaringScope.IsEmpty && _declaringScope.Peek().Item1 >= indent)
					_declaringScope.Pop();
			}
			public IHaveChildScope Peek() => _stack.Peek().Item2;
			public void Push(int indent, IHaveChildScope node)
			{
				_stack.Push(new Tuple<int, IHaveChildScope>(indent, node));
			}
			public void Pop(int indent)
			{
				while (_stack.Peek().Item1 > indent)
					_stack.Pop();
			}

			public void RegisterUsingNamespaceRef(string path, bool onlyApplyInScope)
			{
				if (_usingScope.ToList().Any(p => p.Item2 == path))
					return; // already applying this path

				var depth = _stack.Peek().Item1;
				if (onlyApplyInScope) depth++;
				_usingScope.Push(new Tuple<int, string>(depth, path));
			}
			public List<string> GetUsingNamespaces()
			{
				return _usingScope
					.MultiPeekAll()
					.Select(p => p.Item2)
					.Reverse()
					.ToList();
			}

			public void RegisterDeclaringNamespace(string path, bool onlyApplyInScope, bool isGlobal)
			{
				var depth = _stack.Peek().Item1;
				if (depth < 0)
				{
					var scope = isGlobal ? StaticMapping.DATA : StaticMapping.FILE;
					_declaringScope.Push(new Tuple<int, string>(0, scope));
				}
				if (onlyApplyInScope) depth++;
				_declaringScope.Push(new Tuple<int, string>(depth, path));
			}
			public string GetDeclaringNamespace()
			{
				var steps = _declaringScope.MultiPeekAll().Select(p => p.Item2);
				return util.Join(steps, ".");
			}
		}

		// primarily for the editor to provide completion match on macros
		public IEnumerable<string> IterMacroNames()
		{
			return _macros.Keys;
		}

		public void ClearMacroCacheForFile(string key)
		{
			foreach (var pair in _macros.ToArray())
				if (pair.Value.Item1 == key)
					_macros.Remove(pair.Key);
		}

		public IEnumerable<string> BuiltInMacros()
		{
			yield return "#def";
			yield return "#declares";
			yield return "#requires";
		}

		static IHaveChildScope FindChildScopeInverter(SStatement state)
		{
			IHaveChildScope found = null;
			var list = state.IterExpressionsRecursive();
			foreach (var sub in list)
				if (sub is IHaveChildScope)
				{
					if (found == null) found = sub as IHaveChildScope;
					else throw new CompilerException("ambiguous child scope inversion", state.FileLine);
				}
			return found;
		}
	}

	public struct ParseContext
	{
		public Compiler Comp;
		public WingraBuffer Buffer;
		public int FileLine;
		public ErrorLogger Errors;
		internal Compiler.ScopeStack Scope;
		internal StaticMapping StaticMap => Comp.StaticMap;
		public string FileKey => Buffer.Key;
		internal ParseContext(Compiler comp, WingraBuffer buff, int line, ErrorLogger errors, Compiler.ScopeStack scope)
		{
			Comp = comp;
			Buffer = buff;
			FileLine = line;
			Errors = errors;
			Scope = scope;
		}
		public void LogError(string err, RelativeTokenReference? token = null)
		{
			Errors.LogError(err, ePhase.Parse, FileLine, token);
		}
	}

	// detailed log of scope available at each line for things like completion match
	public class FileScopeTracker
	{
		// this is probably a bad structure - a list would probably make more sense
		Map<int, string> _using = new Map<int, string>();

		internal void ResetLine(int fileLine)
			=> _using.Add(fileLine);
		internal void AddUsing(int fileLine, string path)
			=> _using.Add(fileLine, path); //TODO: de-duplicate? why are there duplciates?

		public List<string> GetPossibleUsing(int line)
		{
			for (int i = line; i >= 0; i--)
			{
				if (_using.Exists(i))
					return _using.Values(i).ToList();
			}
			return new List<string>();
		}
	}
}
