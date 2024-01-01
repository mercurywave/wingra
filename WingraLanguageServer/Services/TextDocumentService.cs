using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JsonRpc.Contracts;
using JsonRpc.Server;
using LanguageServer.VsCode;
using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Server;
using Wingra;
using Wingra.Parser;

namespace WingraLanguageServer.Services
{
	[JsonRpcScope(MethodPrefix = "textDocument/")]
	public class TextDocumentService : LanguageServiceBase
	{
		HashSet<WingraBuffer> _needParse = new HashSet<WingraBuffer>();
		[JsonRpcMethod]
		public async Task<Hover> Hover(TextDocumentIdentifier textDocument, Position position, CancellationToken ct)
		{
			// Note that Hover is cancellable.
			await Task.Delay(100, ct);

			lock (Session.Lock)
			{
				var key = fileUtils.UriTRoPath(textDocument.Uri);
				if (Session.Prj.IsFileLoaded(key))
				{
					var buffer = Session.Prj.GetFile(key);
					if (position.Line < buffer.Lines)
					{
						var scopeTracker = Session._scopeTracker;
						var staticMap = Session._staticMap;

						// I can't think of a good use case for this other than function signatures
						// maybe scratch locations?
						var staticPath = GetPathUnderCursor(position, buffer, out _);
						if (staticPath != "" && scopeTracker.ContainsKey(buffer))
						{
							var tracker = scopeTracker[buffer];

							var prefixes = tracker.GetPossibleUsing(position.Line);
							var result = staticMap.GetJumpToTarget(buffer.Key, staticPath, prefixes);
							if (result != null)
							{
								var absPath = staticMap.GetAbsPath(buffer.Key, staticPath, prefixes, out _);
								TryGetFunctionSig(absPath, out var sig, out _);
								string path;
								if (fileUtils.IsFileInPath(result.Item1, Loader.StdLibPath))
									path = fileUtils.RelativePath(Loader.StdLibPath, result.Item1);
								else if (fileUtils.IsFileInPath(result.Item1, Loader.ExtLibPath))
									path = fileUtils.RelativePath(Loader.ExtLibPath, result.Item1);
								else
									path = fileUtils.RelativePath(Session.Prj.Path, result.Item1);
								return new Hover
								{
									Contents = sig + "\n" + path,
									Range = new LanguageServer.VsCode.Contracts.Range(position, position)
								};
							}
						}
					}
				}
			}

			return null;
			//return new Hover { Contents = "Test _hover_ @" + position + "\n\n" + textDocument, Range = new LanguageServer.VsCode.Contracts.Range(position, position) };
		}


		[JsonRpcMethod]
		public SignatureHelp SignatureHelp(TextDocumentIdentifier textDocument, Position position, object context = null)
		{
			lock (Session.Lock)
			{
				var key = fileUtils.UriTRoPath(textDocument.Uri);
				if (Session.Prj.IsFileLoaded(key))
				{
					var buffer = Session.Prj.GetFile(key);
					if (position.Line < buffer.Lines)
					{
						var scopeTracker = Session._scopeTracker;
						var staticMap = Session._staticMap;

						var funcCall = GetFunctionCall(position, buffer, out var paramIdx);
						if (funcCall != "" && scopeTracker.ContainsKey(buffer))
						{
							var tracker = scopeTracker[buffer];
							var prefixes = tracker.GetPossibleUsing(position.Line);
							var absPath = staticMap.GetAbsPath(buffer.Key, funcCall, prefixes, out _);

							if (TryGetFunctionSig(absPath, out var sig, out var pars))
							{
								return new SignatureHelp(new List<SignatureInformation> {
									new SignatureInformation(sig, new MarkupContent(MarkupKind.PlainText, ""), pars)
								}, 0, paramIdx);
							}
						}
					}
				}
			}
			return new SignatureHelp(new List<SignatureInformation>());
		}

		bool TryGetFunctionSig(string absPath, out string sig, out List<ParameterInformation> pars)
		{
			sig = "";
			pars = new List<ParameterInformation>();
			if (absPath != "" && Session._staticMap.TryGetFunctionInfo(absPath, out var fnName, out var isMethod, out var inputs, out var outputs, out var doesYield, out var isAsync, out var doesThrow, out var isTypeDef))
			{
				sig += "::";
				if (isTypeDef) sig += "%";
				else if (isMethod) sig += ".";
				sig += StaticMapping.GetPathFromAbsPath(absPath);
				if (isTypeDef) return true;
				sig += "(";
				var ins = new List<string>();
				for (int i = 0; i < inputs.Length; i++)
				{
					ins.Add(inputs[i]);
					pars.Add(new ParameterInformation("", new MarkupContent(MarkupKind.Markdown, inputs[i])));
				}
				sig += util.Join(ins, ",");
				if (outputs.Length > 0 || isAsync)
					sig += " => ";
				var retmods = new List<string>();
				if (isAsync) retmods.Add("async");
				if (doesYield) retmods.Add("yield");
				if (doesThrow) retmods.Add("throw");
				if (retmods.Count > 0)
					sig += util.Join(retmods, " ");
				sig += util.Join(outputs, ", ");
				sig += ")";
				return true;
			}
			return false;
		}

		string GetFunctionCall(Position cursor, WingraBuffer buffer, out int currParam)
		{
			var currLineLex = buffer.GetSyntaxMetadata(cursor.Line);
			BaseToken? curr = null;
			var tokes = currLineLex.Tokens;
			int currIdx = currLineLex.Tokens.Count - 1;

			for (int i = 0; i < currLineLex.Tokens.Count; i++)
			{
				var tok = tokes[i];
				if (cursor.Character <= tok.LineOffset)
				{
					currIdx = i - 1;
					break;
				}
				curr = tok;
			}

			int parenStack = 1;
			int parenIdx = -1;
			currParam = 0;
			for (int i = currIdx; i >= 0; i--)
			{
				if (tokes[i].Type == eToken.LeftParen) parenStack--;
				if (tokes[i].Type == eToken.RightParen) parenStack++;
				if (parenStack == 1 && tokes[i].Type == eToken.Comma)
					currParam++;
				if (parenStack == 0)
				{
					parenIdx = i;
					break;
				}
			}

			if (parenIdx <= 0) return "";
			var startSearch = tokes.FindLastIndex(parenIdx, t => t.Type == eToken.StaticIdentifier || t.Type == eToken.TypeIdentifier);
			if (startSearch >= 0)
			{
				var possiblePath = tokes.GetRange(startSearch, tokes.Count - startSearch - 1).ToArray();
				int length = 1;
				for (; length < possiblePath.Length; length++)
				{
					if (length % 2 == 1 && possiblePath[length].Type != eToken.Dot) break;
					if (length % 2 == 0 && possiblePath[length].Type != eToken.Identifier) break;
				}
				var actualPath = tokes.GetRange(startSearch, length).ToArray();
				return util.Join(actualPath.Select(t => t.Token), "").Replace("$", "").Replace("%", "");
			}
			return "";
		}

		[JsonRpcMethod(IsNotification = true)]
		public async Task DidOpen(TextDocumentItem textDocument)
		{
			var doc = new SessionDocument(textDocument);
			var session = Session; // must capture - session not available during callback
			var key = fileUtils.UriTRoPath(doc.Document.Uri);

			lock (session.Lock)
				session.Documents.TryAdd(textDocument.Uri, doc);
			await session.LoadTask;
			if (!session.Prj.IsFileLoaded(key))
				if (fileUtils.IsFileInPath(key, session.Prj.Path))
					if (WingraProject.IsFileWingra(key))
						await session.Prj.AddNewFile(key);

			doc.DocumentChanged += (sender, args) =>
			{
				if (session.Prj.IsFileLoaded(key))
				{
					var wb = session.Prj.GetFile(key);
					lock (session.Lock)
					{
						wb.SyncFromExternal(doc.Document.Content.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None).ToList());
						_needParse.Add(wb);
					}

					_ = Task.Delay(200).ContinueWith(t => Task.Run(() =>
					{
						try
						{
							lock (session.Lock)
							{
								if (_needParse.Count == 0) return;
								foreach (var file in _needParse)
									session.UpdateFileCache(file);
								_needParse.Clear();
							}
						}
						catch (Exception e)
						{
							session.Client.Window.ShowMessage(MessageType.Error, e.ToString());
						}
					}), TaskScheduler.Current);
				}
			};
		}

		[JsonRpcMethod(IsNotification = true)]
		public void DidChange(TextDocumentIdentifier textDocument,
			ICollection<TextDocumentContentChangeEvent> contentChanges)
		{
			lock (Session.Lock)
			{
				Session.Documents[textDocument.Uri].NotifyChanges(contentChanges);
			}
		}

		[JsonRpcMethod(IsNotification = true)]
		public void WillSave(TextDocumentIdentifier textDocument, TextDocumentSaveReason reason)
		{
			if (WingraProject.IsFileWingraProject(fileUtils.UriTRoPath(textDocument.Uri)))
				_ = Session.Rebuild();
		}

		[JsonRpcMethod(IsNotification = true)]
		public async Task DidClose(TextDocumentIdentifier textDocument)
		{
			if (textDocument.Uri.IsUntitled())
			{
				await Client.Document.PublishDiagnostics(textDocument.Uri, new Diagnostic[0]);
			}
			Session.Documents.TryRemove(textDocument.Uri, out _);
		}

		// can't figure out a way to get the correct tabstop
		// probably better to just have the linter detect a mix of tabs and spaces
		//[JsonRpcMethod]
		//public TextEdit[] RangeFormatting(TextDocumentIdentifier textDocument, LanguageServer.VsCode.Contracts.Range range, FormattingOptions options)
		//{
		//	lock (Session.Lock)
		//	{
		//		if (Session.Documents.TryGetValue(textDocument.Uri, out var doc))
		//		{
		//			var text = doc.Document.GetRange(range);
		//			if(text.Contains('\t'))
		//				Session.Client.Workspace.Configuration.
		//				return new TextEdit[] { new TextEdit(range, text.Replace("\t", "     ")) };
		//		}
		//	}
		//	return null;
		//}

		bool IsInTextData(WingraBuffer buffer, int line)
		{
			int indent = int.MaxValue;
			for (int i = line; i >= 0; i--)
			{
				var lex = buffer.GetSyntaxMetadata(i);
				if (lex.PreceedingWhitespace < indent)
				{
					indent = lex.PreceedingWhitespace;
					if (lex.ContainsTextData)
					{
						return true;
					}
				}
				if (lex.PreceedingWhitespace == 0)
					break;
			}
			return false;
		}



		[JsonRpcMethod]
		public CompletionList Completion(TextDocumentIdentifier textDocument, Position position, CompletionContext context)
		{
			lock (Session.Lock)
			{
				var key = fileUtils.UriTRoPath(textDocument.Uri);
				if (Session.Prj.IsFileLoaded(key))
				{
					var buffer = Session.Prj.GetFile(key);
					if (position.Line < buffer.Lines)
					{
						if (IsInTextData(buffer, position.Line))
							return new CompletionList();
						var scopeTracker = Session._scopeTracker;
						var staticMap = Session._staticMap;
						var compiler = Session.Cmplr;
						var parsedFiles = Session._parsedFiles;
						List<CompletionItem> results = new List<CompletionItem>();

						var currLineLex = buffer.GetSyntaxMetadata(position.Line);
						BaseToken? curr = null;
						BaseToken? separator = null;
						BaseToken? prev = null;
						BaseToken? structurePrefix = null;
						string staticPath = "";
						int currIdx = -1;
						for (int i = 0; i < currLineLex.Tokens.Count; i++)
						{
							var tok = currLineLex.Tokens[i];
							if (tok.LineOffset >= position.Character)
							{
								currIdx = i;
								break;
							}
							prev = separator;
							separator = curr;
							curr = tok;
						}

						int LexIndexOfOrEnd(LexLine lex, eToken toke)
						{
							for (int i = 0; i < lex.Tokens.Count; i++)
								if (lex.Tokens[i].Type == toke)
									return i;
							return lex.Tokens.Count;
						}
						// we are in either the function name, parameters, or outputs
						bool inFunctionDeclaration = IsDeclaringFunction(currLineLex, currIdx);
						staticPath = GetPathUnderCursor(position, buffer, out var expectDollar);
						if (curr.HasValue && curr.Value.LineOffset + curr.Value.Length < position.Character)
						{
							separator = curr;
							curr = null;
						}
						if (separator.HasValue
							&& (separator.Value.Type == eToken.FunctionDef
							|| separator.Value.Type == eToken.Enum
							|| separator.Value.Type == eToken.Data
							|| separator.Value.Type == eToken.Template
							|| separator.Value.Type == eToken.Global
							|| separator.Value.Type == eToken.Library
							|| separator.Value.Type == eToken.AtSign))
						{
							return null;
						}

						// thing. => thing.?
						if (curr.HasValue && curr.Value.Type == eToken.Dot)
						{
							prev = separator;
							separator = curr;
							curr = null;
						}
						// new thi...
						if (separator.HasValue
								&& (separator.Value.Type == eToken.New
								|| separator.Value.Type == eToken.Dim
								|| separator.Value.Type == eToken.Mixin))
							structurePrefix = separator;
						// a+b -> b
						if (separator.HasValue && separator.Value.Type != eToken.Dot)
						{
							prev = null;
							separator = null;
						}

						if (curr.HasValue)
						{
							if (curr.Value.Type == eToken.Comment
								|| curr.Value.Type == eToken.LiteralString)
								return null;
						}

						string phrase = "";
						string phraseWithCapitals = "";
						void AddResult(string insert, CompletionItemKind kind, string subtext = "")
						{
							if ((phrase != "$" && phrase != "%") &&
								(kind == CompletionItemKind.Property
								|| kind == CompletionItemKind.Method
								|| kind == CompletionItemKind.Function))
								results.Add(new CompletionItem(insert, kind, subtext, null)
								{
									CommitCharacters = new List<char>() { '.', '(' }
								});
							else
								results.Add(new CompletionItem(insert, kind, subtext, null)
								{
									CommitCharacters = new List<char>() { '.' }
								});
						}

						if (curr.HasValue && scopeTracker.ContainsKey(buffer))
						{
							phraseWithCapitals = curr.Value.Token;
							phrase = phraseWithCapitals.ToLower();

							var tracker = scopeTracker[buffer];

							if (phrase == "$")
							{
								var close = staticMap.SuggestAll(buffer.Key, tracker.GetPossibleUsing(position.Line));

								foreach (var match in close)
								{
									var text = StaticMapping.JoinPath(StaticMapping.SplitPath(match));
									AddResult(match, CompletionItemKind.Module, "$" + text);
								}
							}

							if (phrase == "%")
							{
								var close = staticMap.SuggestAll(buffer.Key, tracker.GetPossibleUsing(position.Line), true);

								foreach (var match in close)
								{
									var text = StaticMapping.JoinPath(StaticMapping.SplitPath(match));
									AddResult(match, CompletionItemKind.Interface, "%" + text);
								}
							}

							if (phrase[0] == '^')
							{
								var name = phrase.Replace("^", "");
								var close = staticMap.SuggestGlobals(name, buffer.Key);

								foreach (var glo in close)
								{
									var text = glo;
									var suggest = glo;
									AddResult(suggest, CompletionItemKind.Field, "^" + text);
								}
							}
						}

						if (staticPath != "" && scopeTracker.ContainsKey(buffer))
						{
							var tracker = scopeTracker[buffer];

							var close = staticMap.SuggestToken(buffer.Key, staticPath, tracker.GetPossibleUsing(position.Line));

							foreach (var match in close)
							{
								var currArr = StaticMapping.SplitPath(staticPath);
								var goal = StaticMapping.SplitPath(match.Key);
								var appender = goal[goal.Length - 1];
								currArr[currArr.Length - 1] = appender;
								//var suggest = "$" + StaticMapping.JoinPath(currArr);
								var suggest = "";
								if (currArr.Length == 1 && expectDollar)
									suggest = appender;
								else suggest = appender;
								var text = StaticMapping.JoinPath(StaticMapping.SplitPath(match.Key));
								bool good = suggest.ToLower().StartsWith(phrase.ToLower());
								CompletionItemKind kind = CompletionItemKind.Module;
								if (match.Value == eStaticType.Constant) kind = CompletionItemKind.Field;
								if (match.Value == eStaticType.Data) kind = CompletionItemKind.Class;
								if (match.Value == eStaticType.EnumValue) kind = CompletionItemKind.Enum;
								if (match.Value == eStaticType.Function) kind = CompletionItemKind.Function;
								if (match.Value == eStaticType.TypeDef) kind = CompletionItemKind.Interface;
								AddResult(suggest, kind);
							}
						}
						if (!separator.HasValue)
						{
							if (!inFunctionDeclaration && !phrase.StartsWith("$") && !phrase.StartsWith("%") && !phrase.StartsWith("#") && !phrase.StartsWith("^"))
								results.AddRange(Session.StaticSuggestions);
							if (phrase.StartsWith("#"))
							{
								//this only handles the 99% case
								foreach (var mac in compiler.IterMacroNames())
									AddResult(mac.Substring(1), CompletionItemKind.Snippet);
								foreach (var mac in compiler.BuiltInMacros())
									AddResult(mac.Substring(1), CompletionItemKind.Snippet);
							}
							HashSet<string> usedTokes = new HashSet<string>();
							void TryAdd(string varName, CompletionItemKind kind, string type)
							{
								if (!usedTokes.Contains(varName))
								{
									usedTokes.Add(varName);
									AddResult(varName, kind, type);
								}
							}
							// this region tries to scan outward in the scope from the cursor, looking for variables that may be accessible
							void NaiveScanForLocals(int lineNumber, LexLine lex)
							{
								var colonSplit = lex.Tokens.FindIndex(t => t.Type == eToken.Colon);
								if (colonSplit < 0) colonSplit = lex.Tokens.Count;
								int lamb = ScanLex(lex, 0, eToken.Lambda, eToken.LeftParen);
								if (lamb >= 0) // if we are in a defined lambda, the available scope is limited
								{
									int end = ScanLex(lex, lamb, eToken.RightParen);
									for (int i = lamb; i < end; i++)
									{
										var tok = lex.Tokens[i];
										if (tok.Type == eToken.Identifier)
											AddResult(tok.Token, CompletionItemKind.Variable, "parameter");
									}
									return;
								}
								for (int j = 0; j < lex.Tokens.Count; j++)
								{
									var tok = lex.Tokens[j];
									if (tok.Type != eToken.Identifier) continue;
									if (!tok.Token.ToLower().StartsWith(phrase)) continue;
									if (usedTokes.Contains(tok.Token)) continue;
									if (lineNumber != position.Line && j > 0 && lex.Tokens[j - 1].Type == eToken.AtSign)
										AddResult(tok.Token, CompletionItemKind.Variable, "local");
									else if (j < lex.Tokens.Count - 1 && lex.Tokens[j + 1].Type == eToken.Colon)
										AddResult(tok.Token, CompletionItemKind.Variable, "local");
									else if (lineNumber != position.Line && j > 2 && j < colonSplit && lex.Tokens[0].Type == eToken.FunctionDef)
										AddResult(tok.Token, CompletionItemKind.Variable, "parameter");
									else if (lineNumber != position.Line && j > 3 && j < colonSplit && lex.Tokens[1].Type == eToken.FunctionDef) // specifically for global ::func()
										AddResult(tok.Token, CompletionItemKind.Variable, "parameter");
									else if (lineNumber != position.Line && j > 2 && j < colonSplit && lex.Tokens[0].Type == eToken.Template) // this isn't a thing anymore...
										AddResult(tok.Token, CompletionItemKind.Variable, "template parameter");
									else continue;
									usedTokes.Add(tok.Token);
								}
								// naive way to look for local functions/properties in the current template
								if (lex.PreceedingWhitespace > 0 && lex.Tokens.Count >= 2)
								{
									var tok = lex.Tokens[1];
									if (tok.Type == eToken.Identifier)
									{
										if (lex.Tokens[0].Type == eToken.Dot)
											AddResult(tok.Token, CompletionItemKind.Property);
										else if (lex.Tokens.Any(t => t.Type == eToken.FunctionDef))
											AddResult(tok.Token, CompletionItemKind.Method);
									}
								}
							}
							int currentIndent = currLineLex.PreceedingWhitespace;
							int highWaterReadAhead = position.Line + 1;
							// reads upwards till it hits file scope
							if (!inFunctionDeclaration && !phrase.StartsWith("$") && !phrase.StartsWith("%") && !phrase.StartsWith("#") && !phrase.StartsWith("^"))
								for (int i = position.Line - 1; i >= 0; i--)
								{
									var lex = buffer.GetSyntaxMetadata(i);
									if (lex.PreceedingWhitespace > currentIndent || lex.IsEmpty)
										continue;
									else if (lex.PreceedingWhitespace == currentIndent)
									{
										if (lex.Tokens.Any(t => t.Type == eToken.FunctionDef)) continue;
										if (ScanLex(lex, 0, eToken.Lambda, eToken.LeftParen) >= 0) continue;
										NaiveScanForLocals(i, lex);
									}
									else if (lex.PreceedingWhitespace < currentIndent)
									{
										NaiveScanForLocals(i, lex);
										currentIndent = lex.PreceedingWhitespace;
										if (lex.Tokens.Any(t => t.Type == eToken.FunctionDef)) break;
										if (ScanLex(lex, 0, eToken.Lambda, eToken.LeftParen) >= 0) break;
										// scan ahead at the same scope we found when we collapsed a level
										for (; highWaterReadAhead < buffer.Lines; highWaterReadAhead++)
										{
											var readAhead = buffer.GetSyntaxMetadata(highWaterReadAhead);
											if (readAhead.PreceedingWhitespace < currentIndent)
												break;
											if (readAhead.PreceedingWhitespace > currentIndent)
												continue;
											NaiveScanForLocals(highWaterReadAhead, readAhead);
										}
										if (lex.Tokens[0].Type == eToken.For)
											TryAdd("it", CompletionItemKind.Variable, "local");
										else if (lex.Tokens[0].Type == eToken.Trap)
											TryAdd("error", CompletionItemKind.Variable, "local");
									}
									if (currentIndent == 0 && currentIndent > 0) break;

								}

						}


						return new CompletionList(results);
					}
				}
			}
			return new CompletionList(Session.StaticSuggestions);
		}

		bool IsDeclaringFunction(LexLine lex, int currIdx)
		{
			// this basically exists to stop suggesting things if you're clearly declaring something new
			int Scan(int from, params eToken[] matches)
				=> ScanLex(lex, from, matches);

			bool IsBetween(eToken[] start, eToken ender)
			{
				int idx = 0;
				while (idx >= 0)
				{
					idx = Scan(idx, start);
					if (idx < 0 || currIdx < idx) return false; // passed the cursor
					var end = Scan(idx, ender);
					if (end < 0 || currIdx <= end) return true;
					idx++;
				}
				return false;
			}

			eToken[] tl(params eToken[] tokes) => tokes;

			return IsBetween(tl(eToken.FunctionDef, eToken.Identifier, eToken.LeftParen), eToken.RightParen)
				|| IsBetween(tl(eToken.FunctionDef), eToken.LeftParen)
				|| IsBetween(tl(eToken.Lambda, eToken.LeftParen), eToken.RightParen);
		}

		int ScanLex(LexLine lex, int from, params eToken[] matches)
		{
			if (from < 0) return -1;
			for (int i = from; i < lex.Tokens.Count; i++)
			{
				bool found = true;
				for (int j = 0; j < matches.Length && j + i < lex.Tokens.Count; j++)
				{
					if (lex.Tokens[j + i].Type != matches[j])
						found = false;
				}
				if (found) return i;
			}
			return -1;
		}




		string GetPathUnderCursor(Position cursor, WingraBuffer buffer, out bool shouldUseDollar)
		{
			var currLineLex = buffer.GetSyntaxMetadata(cursor.Line);
			BaseToken? curr = null;
			var tokes = currLineLex.Tokens;
			int currIdx = tokes.Count - 1;
			shouldUseDollar = true;
			for (int i = 0; i < currLineLex.Tokens.Count; i++)
			{
				var tok = tokes[i];
				if (tok.LineOffset + tok.Length >= cursor.Character)
				{
					currIdx = i;
					break;
				}
				curr = tok;
			}

			var startSearch = tokes.FindLastIndex(currIdx, t => t.Type == eToken.StaticIdentifier || t.Type == eToken.TypeIdentifier);

			if (startSearch < 0)
			{
				startSearch = tokes.FindLastIndex(currIdx, t => t.Type == eToken.Using);
				if (startSearch >= 0 && currIdx != startSearch) startSearch++;
			}

			if (startSearch >= 0)
			{
				if (startSearch > 0)
					shouldUseDollar = (tokes[startSearch - 1].Type != eToken.Using);
				if (startSearch >= tokes.Count) return "";
				var possiblePath = tokes.GetRange(startSearch, tokes.Count - startSearch).ToArray();
				int length = 1;
				for (; length < possiblePath.Length; length++)
				{
					if (length % 2 == 1 && possiblePath[length].Type != eToken.Dot) break;
					if (length % 2 == 0 && possiblePath[length].Type != eToken.Identifier) break;
				}
				if (startSearch + length <= currIdx) return "";
				var actualPath = tokes.GetRange(startSearch, length).ToArray();
				return util.Join(actualPath.Select(t => t.Token), "").Replace("$", "").Replace("%", "");
			}
			return "";
		}

		[JsonRpcMethod(methodName: "definition")]
		public async Task<Location> Definition(TextDocumentIdentifier textDocument, Position position, CancellationToken ct)
		{
			lock (Session.Lock)
			{
				var key = fileUtils.UriTRoPath(textDocument.Uri);
				if (Session.Prj.IsFileLoaded(key))
				{
					var buffer = Session.Prj.GetFile(key);
					if (position.Line < buffer.Lines)
					{
						var scopeTracker = Session._scopeTracker;
						var staticMap = Session._staticMap;

						var staticPath = GetPathUnderCursor(position, buffer, out _);
						if (staticPath != "" && scopeTracker.ContainsKey(buffer))
						{
							var tracker = scopeTracker[buffer];

							var prefixes = tracker.GetPossibleUsing(position.Line);
							var result = staticMap.GetJumpToTarget(buffer.Key, staticPath, prefixes);
							if (result != null)
							{
								int cPos = 0;
								if (Session.Prj.IsFileLoaded(result.Item1))
								{
									var targ = Session.Prj.GetFile(result.Item1);
									var lex = targ.GetSyntaxMetadata(result.Item2);
									if (lex.Tokens.Count > 0)
										cPos = lex.Tokens[0].LineOffset;
									foreach (var tok in lex.Tokens)
										if (tok.Token.Contains(staticPath))
											cPos = tok.LineOffset;
									if (cPos == 0)
									{
										var text = targ.TextAtLine(result.Item2);
										if (text.Length > 0)
											cPos = Array.FindIndex(text.ToCharArray(), c => !char.IsWhiteSpace(c));
									}
								}
								var pos = new Position(result.Item2, cPos);
								return new Location()
								{
									Uri = fileUtils.FileToUri(result.Item1),
									Range = new LanguageServer.VsCode.Contracts.Range(pos, pos),
								};
							}
						}
					}
				}
			}
			return new Location() { Uri = textDocument.Uri, Range = new LanguageServer.VsCode.Contracts.Range(position, position) };
		}
	}

}
