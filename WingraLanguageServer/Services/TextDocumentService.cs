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
			if (absPath != "" && Session._staticMap.TryGetFunctionInfo(absPath, out var fnName, out var isMethod, out var inputs, out var outputs, out var doesYield, out var isAsync))
			{
				sig += "::";
				if (isMethod) sig += ".";
				sig += StaticMapping.GetPathFromAbsPath(absPath);
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
				if (isAsync) sig += " async ";
				if (doesYield) sig += " yield ";
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
			int currIdx = -1;

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
			var startSearch = tokes.FindLastIndex(parenIdx, t => t.Type == eToken.StaticIdentifier);
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
				return util.Join(actualPath.Select(t => t.Token), "").Replace("$", "");
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

					_ = Task.Delay(200).ContinueWith(t => Task.Run(async () =>
					{
						try
						{
							ICollection<Diagnostic> diag;
							lock (session.Lock)
							{
								if (_needParse.Count == 0) return;
								foreach (var file in _needParse)
									session.UpdateFileCache(file);
								_needParse.Clear();

								var prj = session.Prj;
								if (!prj.CheckForErrors())
								{
									prj.CompileAll(session.Cmplr);
								}

								var prov = session.DiagnosticProvider;
								diag = prov.LintDocument(session, key);
							}
							await session.Client.Document.PublishDiagnostics(doc.Document.Uri, diag);
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
			// TODO: changing the wingraProj file should probably trigger some sort of reload, or at least a warning

			//Client.Window.LogMessage(MessageType.Log, "-----------");
			//Client.Window.LogMessage(MessageType.Log, Documents[textDocument].Content);
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
						//Debug(buffer.TextAtLine(position.Line));
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
							return new CompletionList();
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
								return new CompletionList();
						}

						string phrase = "";
						string phraseWithCapitals = "";
						void AddResult(string insert, CompletionItemKind kind, string subtext = "")
						{
							if (phrase != "$" &&
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
								AddResult(suggest, kind);
							}
						}
						if (!separator.HasValue)
						{
							if (!phrase.StartsWith("$"))
								results.AddRange(Session.StaticSuggestions);
							if (phrase.StartsWith("#"))
							{
								//this only handles the 99% case
								foreach (var mac in compiler.IterMacroNames())
									AddResult(mac, CompletionItemKind.Snippet);
								foreach (var mac in compiler.BuiltInMacros())
									AddResult(mac, CompletionItemKind.Snippet);
							}
							// this region tries to scan outward in the scope from the cursor, looking for variables that may be accessible
							void NaiveScanForLocals(int lineNumber, LexLine lex)
							{
								var colonSplit = lex.Tokens.FindIndex(t => t.Type == eToken.Colon);
								if (colonSplit < 0) colonSplit = lex.Tokens.Count;
								for (int j = 0; j < lex.Tokens.Count; j++)
								{
									var tok = lex.Tokens[j];
									if (tok.Type != eToken.Identifier) continue;
									if (!tok.Token.ToLower().StartsWith(phrase)) continue;
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
								}
								// naive way to look for local functions/properties in the current template
								if (lex.PreceedingWhitespace > 0 && lex.Tokens.Count >= 2)
								{
									var tok = lex.Tokens[1];
									if (tok.Type == eToken.Identifier)
									{
										if (lex.Tokens[0].Type == eToken.Dot)
											AddResult(tok.Token, CompletionItemKind.Property);
										else if (lex.Tokens[0].Type == eToken.FunctionDef)
											AddResult(tok.Token, CompletionItemKind.Method);
									}
								}
							}
							int currentIndent = currLineLex.PreceedingWhitespace;
							int highWaterReadAhead = position.Line + 1;
							// reads upwards till it hits file scope
							if (!phrase.StartsWith("$"))
								for (int i = position.Line - 1; i >= 0; i--)
								{
									var lex = buffer.GetSyntaxMetadata(i);
									if (lex.PreceedingWhitespace > currentIndent || lex.IsEmpty)
										continue;
									else if (lex.PreceedingWhitespace == currentIndent)
										NaiveScanForLocals(i, lex);
									else if (lex.PreceedingWhitespace < currentIndent)
									{
										NaiveScanForLocals(i, lex);
										currentIndent = lex.PreceedingWhitespace;
										if (currentIndent == 0) break;
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
									}
									if (currentIndent == 0) break;
								}

							foreach (var pair in parsedFiles)
							{
								if (pair.Key == buffer)
								{
									foreach (var fileChild in pair.Value.Children)
									{
										//if (fileChild is SfunctionDef)
										//MaybeAdd((fileChild as SfunctionDef).Identifier, pair.Key.ShortFileName, eMatchQuality.Good);
										if (fileChild is IDeclareVariablesAtScope)
											foreach (var sub in (fileChild as IDeclareVariablesAtScope).GetDeclaredSymbolsInside(pair.Value))
												AddResult(sub, CompletionItemKind.Variable, "local");
									}

								}
								else
									foreach (var fileChild in pair.Value.Children)
									{
										if (fileChild is IExportGlobalSymbol)
											foreach (var symbol in (fileChild as IExportGlobalSymbol).GetExportableSymbolsInside(fileChild).ToArray())
												AddResult(symbol, CompletionItemKind.Field, pair.Key.ShortFileName); // is this a thing?
									}
							}

						}


						return new CompletionList(results);
					}
				}
			}
			return new CompletionList(Session.StaticSuggestions);
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

			var startSearch = tokes.FindLastIndex(currIdx, t => t.Type == eToken.StaticIdentifier);

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
				return util.Join(actualPath.Select(t => t.Token), "").Replace("$", "");
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
