using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	delegate MatchResult MatchHandler(ParseContext context, RelativeTokenReference[] tokens, int begin);
	delegate SyntaxNode TreeNodeGenerator(Result preparse);
	class ChainList : List<SingleMatch>
	{
		public static ChainList operator +(ChainList a, SingleMatch b) { a.Add(b); return a; }
		public static ChainList operator +(ChainList a, ChainList b) { a.AddRange(b); return a; }
		public static implicit operator ChainList(SingleMatch chain) { return new ChainList() { chain }; }
	}
	struct SingleMatch // this dumb wraper is needed because adding delegates takes precidence over the chainlist
	{
		public MatchHandler Handle;
		public bool Optional;
		public SingleMatch(MatchHandler handle, bool optional = false) { Handle = handle; Optional = optional; }
	}

	public struct MatchResult
	{
		public bool Match;
		public int Begin; //inclusive first token
		public int End; // inclusive final token index
		public string Key;
		public MatchResult(bool match, int begin, int end, string key = "") { Match = match; Begin = begin; End = end; Key = key; }
		public MatchResult(bool match = false) { Match = match; Begin = 0; End = 0; Key = null; }
	}

	public struct Result
	{
		List<MatchResult> Results;
		RelativeTokenReference[] Tokens;
		public ParseContext Context;
		public Compiler Compiler => Context.Comp;
		public string FileKey => Context.FileKey;
		public int FileLine => Context.FileLine;
		public WingraBuffer Buffer => Context.Buffer;
		public ErrorLogger Errors => Context.Errors;
		public List<string> GetUsingScope() => Context.Scope.GetUsingNamespaces();
		public string GetDeclaringNamespace() => Context.Scope.GetDeclaringNamespace();
		public Result(ParseContext context, List<MatchResult> res, RelativeTokenReference[] tokens)
		{ Context = context; Results = res; Tokens = tokens; }

		public bool KeyMatched(string key)
		{
			foreach (var r in Results)
				if (r.Key == key)
					return r.Match;
			return false;
		}
		public RelativeTokenReference GetToken(string key)
		{
			foreach (var r in Results)
				if (r.Key == key)
					return Tokens[r.Begin];
			throw new Exception("Internal parse error: result lost during preparse");
		}

		public RelativeTokenReference GetFirstToken(eToken token)
		{
			foreach (var t in Tokens)
				if (t.Token.Type == token)
					return t;
			throw new Exception("Internal parse error: result lost during preparse");
		}

		public RelativeTokenReference GetFirstToken()
		{
			return Tokens[0];
		}

		public bool HasToken(string key)
		{
			return Results.Any(t => t.Key == key);
		}

		public RelativeTokenReference[] GetTokens(string key)
		{
			foreach (var r in Results)
				if (r.Key == key)
					return Tokens.Skip(r.Begin).Take(r.End - r.Begin + 1).ToArray(); //+1 because the end is inclusive
			throw new Exception("Internal parse error: result lost during preparse");
		}

		public RelativeTokenReference[] GetCleanedPath(string key = "path")
		{
			var tokes = GetTokens(key);
			return SStaticPath.CleanPath(tokes);
		}
	}
}
