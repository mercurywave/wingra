using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	public struct LexLine
	{
		public List<BaseToken> Tokens;
		public bool LineIsContinuation;
		public int PreceedingWhitespace;
		public bool IsEmpty => Tokens.Count == 0;
		public bool ContainsMacro;
		public bool DefinesMacro => Tokens.Count > 0 && Tokens[0].Type == eToken.MacroDef;
		public bool ContainsTextData => Tokens.Any(t => t.Type == eToken.TextData);
		public LexLine(string text, int tabWidth)
		{
			Tokens = new List<BaseToken>();
			PreceedingWhitespace = 0;
			LineIsContinuation = false;
			ContainsMacro = false;
			Process(text, tabWidth);
		}
		struct MatchPair
		{
			public string Toke;
			public string LookFor;
			public eToken Begin;
			public eToken End;
			public MatchPair(string toke, string lookFor, eToken begin, eToken end)
			{
				Toke = toke;
				LookFor = lookFor;
				Begin = begin;
				End = end;
			}
		}
		static Dictionary<string, MatchPair> _matchPairs = new Dictionary<string, MatchPair>() {
			{ "$\"", new MatchPair("$\"", "\"", eToken.BeginInterpString, eToken.EndString) },
			{ "\"", new MatchPair("\"", "\"", eToken.BeginString, eToken.EndString) },
			{ "{", new MatchPair("{", "}", eToken.LeftBrace, eToken.RightBrace) },
			{ "(", new MatchPair("(", ")", eToken.LeftParen, eToken.RightParen) },
		};
		public LexLine Process(string text, int tabWidth)
		{
			Tokens.Clear();
			PreceedingWhitespace = 0;
			LineIsContinuation = false;
			ContainsMacro = false;
			int leadingSpaceChars = 0;
			// line leaders
			{
				for (int i = 0; i < text.Length; i++)
				{
					if (text[i] == ' ') PreceedingWhitespace += 1;
					else if (text[i] == '\t') PreceedingWhitespace += tabWidth;
					else break;
					leadingSpaceChars++;
				}
			}
			// code
			{
				int i = leadingSpaceChars;
				Stack<MatchPair> expectedPairs = new Stack<MatchPair>();

				bool commentRemainder = false;
				bool macroRemainder = false;
				bool first = true;
				while (i < text.Length && !commentRemainder && !macroRemainder)
				{
					bool instring = expectedPairs.Count > 0 && expectedPairs.Peek().End == eToken.EndString;
					bool inInterpString = expectedPairs.Count > 0 && expectedPairs.Peek().Begin == eToken.BeginInterpString;
					string token = Scan(text, i, instring, inInterpString, out var whiteSpace);
					if (token.Length == 0)
					{
						i += 1 + whiteSpace;
						continue;
					}
					i += whiteSpace;
					eToken type = eToken.Unknown;

					void PushPair(string toke)
					{
						var match = _matchPairs[toke];
						type = match.Begin;
						expectedPairs.Push(match);
					}

					if (inInterpString && token == "{")
						PushPair(token);
					else if (expectedPairs.Count > 0 && token == expectedPairs.Peek().LookFor)
					{
						type = expectedPairs.Peek().End;
						expectedPairs.Pop();
					}
					else if (instring)
						type = eToken.LiteralString;
					else if (_matchPairs.ContainsKey(token))
						PushPair(token);
					else if (_tokenMap.ContainsKey(token))
						type = _tokenMap[token];
					else
					{
						double temp;
						if (double.TryParse(token, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out temp))
							type = eToken.LiteralNumber;
						else if (IsStartOfIdentifier(token[0]))
						{
							if (token[0] == '$') type = eToken.StaticIdentifier;
							else if (token[0] == '^') type = eToken.GlobalIdentifier;
							else if (token[0] == '#') type = eToken.Macro;
							else if (token[0] == '%') type = eToken.TypeIdentifier;
							else type = eToken.Identifier;
						}
					}
					if (BaseToken.IsTokenDirective(type)) macroRemainder = true;
					else if (type == eToken.CommentBegin) commentRemainder = true;

					if (first && type == eToken.LineContinuation)
						LineIsContinuation = true;
					else
						Tokens.Add(new BaseToken(type, token, i));

					i += token.Length;
					first = false;
				}
				if (i < text.Length)
				{
					if (macroRemainder) Tokens.Add(new BaseToken(eToken.Macro, text.Substring(i), i));
					else if (commentRemainder) Tokens.Add(new BaseToken(eToken.Comment, text.Substring(i), i));
				}
				ContainsMacro = macroRemainder;
			}
			return this;
		}

		bool IsStartOfIdentifier(char c) => char.IsLetter(c) || c == '_' || c == '#' || c == '$' || c == '^' || c == '%';

		string Scan(string text, int begin, bool inString, bool inInterpString, out int whiteSpaceSkipped) // TODO: infix string stuff
		{
			whiteSpaceSkipped = 0;
			if (text.Length <= begin) return "";
			char c = text[begin];
			if (inString || inInterpString)
			{
				if (c == '"') return "\"";
				if (inInterpString && c == '{') return "{";
				int idx = begin;
				bool escape = false;
				while (idx < text.Length)
				{
					var readAhead = text[idx];

					if (!escape && readAhead == '"')
						return text.Substring(begin, idx - begin);
					if (inInterpString && !escape && readAhead == '{')
						return text.Substring(begin, idx - begin);

					if (escape) escape = false;
					else if (readAhead == '\\') escape = true;
					idx++;
				}
				return text.Substring(begin); // hit the end
			}
			else
			{
				while (c == ' ')
				{
					begin++; whiteSpaceSkipped++;
					if (begin >= text.Length) return "";
					c = text[begin];
				}
			}
			int i = begin + 1;
			string token = "" + c;
			if (char.IsDigit(c) || (c == '.' && begin < text.Length - 1 && char.IsDigit(text[i]))) // note to self, don't try to lex negative numbers as a literal. it breaks subtraction like (a-4)
			{
				for (; i < text.Length; i++)
				{
					char j = text[i];
					if (char.IsDigit(j)) token += j;
					else if (j == '.') token += j;
					else break;
				}
				return token;
			}
			if (IsStartOfIdentifier(c))
			{
				for (; i < text.Length; i++)
				{
					char j = text[i];
					if (char.IsLetterOrDigit(j) || j == '_') token += j;
					else if (token == "$" && j == '"')
						return "$\""; // special case to detect $" as a single token
					else break;
				}
				return token;
			}

			//special searchs for multi-character tokens
			//there probably aren't enough of these to warrant a generalized approach
			if (begin < text.Length - 1)
			{
				if (c == '/' && text[i] == '/')
					return "//"; // comment
				if (c == ':' && text[i] == '=')
					return ":=";
				if (c == '.' && text[i] == '?')
					return ".?";
				if (c == '?' && text[i] == '.')
					return "?.";
				if (c == ':' && text[i] == ':')
					return "::";
				if (c == '!' && text[i] == '=')
					return "!=";
				if (c == '$' && text[i] == '"')
					return "$\"";
				if (c == '$' && !char.IsLetter(text[i]))
					return "$";
				if (c == '%' && !char.IsLetter(text[i]))
					return "%";
				if (c == '<' && text[i] == '=')
					return "<=";
				if (c == '>' && text[i] == '=')
					return ">=";
				if (c == '<' && text[i] == '<')
					return "<<";
				if (c == '>' && text[i] == '>')
					return ">>";
				if (c == '=' && text[i] == '>')
					return "=>";
			}

			// else, return single char
			return token;
		}

		public static Dictionary<string, eToken> _tokenMap = new Dictionary<string, eToken>() {
			{ "(", eToken.LeftParen },
			{ ")", eToken.RightParen},
			{ "[", eToken.LeftBracket },
			{ "]", eToken.RightBracket },
			{ "{", eToken.LeftBrace },
			{ "}", eToken.RightBrace },
			{ "if", eToken.If },
			{ "else", eToken.Else },
			{ "switch", eToken.Switch },
			{ "select", eToken.Select},
			{ "case", eToken.Case},
			{ "for", eToken.For },
			{ "to", eToken.To },
			{ "by", eToken.By },
			{ "at", eToken.At },
			{ "in", eToken.In },
			{ "of", eToken.Of },
			{ "has", eToken.Has },
			{ "copy", eToken.Copy },
			{ "free", eToken.Free },
			{ "is", eToken.Is },
			{ "isnt", eToken.Isnt },
			{ "while", eToken.While },
			{ "until", eToken.Until },
			{ "break", eToken.Break },
			{ "continue", eToken.Continue },
			{ "return", eToken.Return },
			{ "yield", eToken.Yield },
			{ "quit", eToken.Quit },
			{ ".", eToken.Dot },
			{ "this", eToken.This },
			{ "new", eToken.New },
			{ "dim", eToken.Dim },
			{ "=>", eToken.Arrow },
			{ "<<", eToken.ExpAssignLeft },
			{ ">>", eToken.ExpAssignRight },
			{ "mixin", eToken.Mixin },
			{ "using", eToken.Using },
			{ ",", eToken.Comma },
			{ "template", eToken.Template },
			{ "true", eToken.True },
			{ "false", eToken.False },
			{ "null", eToken.Null },
			{ "textdata", eToken.TextData },
			{ "library", eToken.Library },
			{ "data", eToken.Data },
			{ "enum", eToken.Enum },
			{ "use", eToken.Import },
			{ "global", eToken.Global },
			{ "scratch", eToken.Scratch },
			{ "registry", eToken.Registry },
			{ "namespace", eToken.Namespace },
			{ "extern", eToken.Extern },
			{ "trap", eToken.Trap },
			{ "try", eToken.Try },
			{ "catch", eToken.Catch },
			{ "throw", eToken.Throw },
			{ "avow", eToken.Avow },
			{ "async", eToken.Async },
			{ "await", eToken.Await },
			{ "arun", eToken.Arun },
			{ "defer", eToken.Defer },
			{ "@", eToken.AtSign },
			{ "_", eToken.LineContinuation },
			{ "!=", eToken.NotEquals },
			{ "=", eToken.EqualSign },
			{ "?", eToken.QuestionMark },
			{ ".?", eToken.DotQuestion },
			{ "?.", eToken.QuestionDot },
			{ "<", eToken.Less },
			{ "<=", eToken.EqLess },
			{ ">", eToken.Greater },
			{ ">=", eToken.EqGreater },
			{ "&", eToken.And},
			{ "|", eToken.Or },
			{ "!", eToken.Not },
			{ ":", eToken.Colon },
			{ "$", eToken.Dollar },
			{ "%", eToken.Percent },
			{ ";", eToken.SemiColon },
			{ "\\", eToken.BackSlash },
			{ "::", eToken.FunctionDef},
			{ "+", eToken.Add },
			{ "-", eToken.Subtract},
			{ "*", eToken.Multiply},
			{ "/", eToken.Divide},
			{ "//", eToken.CommentBegin},
			{ "`", eToken.OneLiner},
			{ "~", eToken.Meh},
			{ "lambda", eToken.Lambda },
			{ "#def", eToken.MacroDef },
			{ "#bootstrap", eToken.BootStrap },
			{ "#declares", eToken.Declare },
			{ "#requires", eToken.Require },
		};
		//public static Dictionary<eToken, string> _tokeToSymbol = _tokenMap.ToDictionary(p => p.Value, p => p.Key);

		static bool _validateTokensDummy = _validateDummyFunc();
		static bool _validateDummyFunc()
		{
			foreach (var truth in BaseToken.MappedSymbols())
				if (!_tokenMap.ContainsValue(truth))
					throw new Exception("forgot to define token - " + truth.ToString());
			return true;
		}

		public IEnumerable<BaseToken> GetMeaningfulTokens()
		{
			for (int i = 0; i < Tokens.Count; i++)
			{
				var t = Tokens[i];
				if (t.IsRealCode())
					yield return t;
			}
		}

		public bool ShouldNextLineProbablyBeIndented()
		{
			var list = GetMeaningfulTokens();
			var tk = list.FirstOrDefault();
			if (tk.Type == eToken.Unknown) return false;
			if (!list.Any(t => t.DoesSymbolImplyIndent())) return false;
			if (list.Any(t => t.Type == eToken.BackSlash)) return false;
			return true;
		}

		// there's some slight fudging - this is primarily for mouse peeking
		public BaseToken? GetTokenAtOffset(int x)
		{
			for (int i = Tokens.Count - 1; i > 0; i--)
			{
				var tk = Tokens[i];
				if (x >= tk.LineOffset && x < tk.LineOffset + tk.Length + 1)
					return tk;
			}
			return null;
		}

		public List<RelativeTokenReference> GetRealRelativeTokens(int subline)
		{
			List<RelativeTokenReference> list = new List<RelativeTokenReference>();
			foreach (var tk in Tokens)
				if (tk.IsRealCode())
					list.Add(new RelativeTokenReference(tk, subline));
			return list;
		}

		// to relatively unindent for macros 
		public static string RemovePreceedingWhitespace(string text, int whitespace, int tabWidth)
		{
			int i;
			for (i = 0; i < text.Length && whitespace > 0; i++)
			{
				if (text[i] == ' ') whitespace--;
				else if (text[i] == '\t') whitespace -= tabWidth;
			}
			return util.BoundedSubstr(text, i, text.Length - i);
		}

		#region string helpers
		static string RepeatString(string str, int count)
		{
			for (int i = 0; i < count; i++)
				str += str;
			return str;
		}
		public static string AppendPiece(string start, string splitter, string append)
		{
			if (start == "") return append;
			return start + splitter + append;
		}

		public override string ToString()
		{
			string line = "";
			Tokens.ForEach(t => line = AppendPiece(line, " ", t.Token));
			return line.Replace('\t', ' ');
		}
		#endregion
	}
}
