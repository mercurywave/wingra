using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	static class LineParser
	{

		#region patterns
		struct SyntaxPattern
		{
			public ChainList Match;
			public TreeNodeGenerator Generator;
		}
		static SyntaxPattern mp(ChainList match, TreeNodeGenerator gen)
			=> new SyntaxPattern() { Generator = gen, Match = match };


		static SyntaxPattern mm =
			mp(Token(eToken.If) + SimpleExpression(),
				res => new SIfStatment(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))));

		static List<SyntaxPattern> StatementChains = new List<SyntaxPattern>()
		{
			mp(Token(eToken.If) + SimpleExpression(),
				res => new SIfStatment(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value")))),

			mp(Token(eToken.Else) + Token(eToken.If) + SimpleExpression(),
				res => new SElseIfStatment(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),

			mp(Token(eToken.Else),
				res => new SElseStatment(res.FileLine)),

			mp(Token(eToken.Return) + ExpressionList(),
				res => new SReturn(res.FileLine,
					ExpressionParser.ParseExpressionList(res.Context, res.GetTokens("list"))) ),

			mp(Token(eToken.Arrow) + ExpressionList(),
				res => new SArrowReturnStatement(res.FileLine,
					ExpressionParser.ParseExpressionList(res.Context, res.GetTokens("list"))) ),

			mp(Token(eToken.Return),
				res => throw new ParserException("'return' requires defined outputs, use 'quit' instead", res.GetFirstToken())),

			mp(Token(eToken.Yield) + ExpressionList(),
				res => new SYield(res.FileLine,
					ExpressionParser.ParseExpressionList(res.Context, res.GetTokens("list"))) ),

			// for iter : lower to upper by num
			mp(Token(eToken.For) + SimpleExpression("iter", eToken.Colon) + Token(eToken.Colon) + SimpleExpression("lower", eToken.To) + Token(eToken.To) + SimpleExpression("upper", eToken.By) + Token(eToken.By) + SimpleExpression("by"),
				res => new SForLoop(res.FileLine
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("iter"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("lower"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("upper"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("by"))) ),
			// for iter : lower to upper
			mp(Token(eToken.For) + SimpleExpression("iter", eToken.Colon) + Token(eToken.Colon) + SimpleExpression("lower", eToken.To) + Token(eToken.To) + SimpleExpression("upper"),
				res => new SForLoop(res.FileLine
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("iter"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("lower"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("upper"))) ),

			// for lower to upper by num
			mp(Token(eToken.For) + SimpleExpression("lower", eToken.To) + Token(eToken.To) + SimpleExpression("upper", eToken.By) + Token(eToken.By) + SimpleExpression("by"),
				res => new SForLoop(res.FileLine, null
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("lower"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("upper"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("by"))) ),
			// for lower to upper
			mp(Token(eToken.For) + SimpleExpression("lower", eToken.To) + Token(eToken.To) + SimpleExpression("upper"),
				res => new SForLoop(res.FileLine, null
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("lower"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("upper"))) ),

			
			// for vars : iterator
			mp(Token(eToken.For) + ExpressionList("vars", eToken.Colon) + Token(eToken.Colon) + SimpleExpression("iterator"),
				res => new SForIterate(res.FileLine
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("iterator"))
					,  ExpressionParser.ParseExpressionList(res.Context, res.GetTokens("vars"))) ),
			
			// for elem of list at idx
			mp(Token(eToken.For) + SimpleExpression("elem", eToken.Of) + Token(eToken.Of) + SimpleExpression("list", eToken.At) + Token(eToken.At) + SimpleExpression("idx"),
				res => new SForOf(res.FileLine
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("list"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("elem"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("idx"))) ),
			// for elem of list
			mp(Token(eToken.For) + SimpleExpression("elem", eToken.Of) + Token(eToken.Of) + SimpleExpression(),
				res => new SForOf(res.FileLine
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("elem"))) ),

			// for key in expr
			mp(Token(eToken.For) + SimpleExpression("key", eToken.In) + Token(eToken.In) + SimpleExpression(),
				res => new SForIn(res.FileLine
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))
					, ExpressionParser.ParseExpression(res.Context, res.GetTokens("key"))) ),

			// for expr
			mp(Token(eToken.For) + SimpleExpression(),
				res => new SForIterate(res.FileLine, ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),

			mp(Token(eToken.Using) + Path(false),
				res => new SUsing(res.FileLine, new SStaticPath(res.GetCleanedPath())) ),

			mp(Token(eToken.Switch) + SimpleExpression(),
				res => new SSwitchStatement(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),
			mp(Token(eToken.Switch),
				res => new SSwitchStatement(res.FileLine) ),
			mp(Token(eToken.Quit),
				res => new SQuit(res.FileLine)),
			mp(Token(eToken.While) + SimpleExpression(),
				res => new SWhile(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),
			mp(Token(eToken.Break),
				res => new SBreak(res.FileLine)),
			mp(Token(eToken.Continue),
				res => new SContinue(res.FileLine)),

			mp(Token(eToken.Defer) + Nothing(),
				res => new SDefer(res.FileLine)),
			mp(Token(eToken.Defer) + AntiPeek(eToken.Defer) + Anything(),
				res => new SDeferStatement(res.FileLine,
					ParseMultiStatement(res.Context, res.GetTokens("ANYTHING"))) ),

			mp(Token(eToken.Trap) + AntiPeek(eToken.Trap) + Anything(),
				res => new STrapStatement(res.FileLine,
					ParseMultiStatement(res.Context, res.GetTokens("ANYTHING"))) ),
			mp(Token(eToken.Throw) + SimpleExpression(),
				res => new SThrowStatement(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),
			mp(Token(eToken.Throw),
				res => new SThrowStatement(res.FileLine) ),
			mp(SimpleExpression("ident", eToken.QuestionMark, eToken.Add, eToken.Subtract, eToken.Multiply, eToken.Divide, eToken.And, eToken.Or) 
				+ AnyOf("op", eToken.QuestionMark, eToken.Add, eToken.Subtract, eToken.Multiply, eToken.Divide, eToken.And, eToken.Or)
				+ Token(eToken.Colon) + SimpleExpression("value"),
				res => new SOpAssign(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("ident")),
					res.GetToken("op").Token.Type,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value")))),
			mp(Token(eToken.LeftParen) + ExpressionList("targets", eToken.RightParen) + Token(eToken.RightParen)
				+ Token(eToken.Colon) + SimpleExpression("value"),
				res => {
					var left = ExpressionParser.ParseExpressionList(res.Context, res.GetTokens("targets"), eToken.Colon);
					var right = ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"));
					var mult = new List<SExpressionComponent>(){ new SMultiAssignExp(left) };
					return new SAssign(res.FileLine, mult , eToken.Colon, right);
				}),
			mp( ExpressionList("targets",eToken.Colon) + Token(eToken.Colon) + SimpleExpression("value"),
				res => {
					var left = ExpressionParser.ParseExpressionList(res.Context, res.GetTokens("targets"), eToken.Colon);
					var right = ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"));
					return new SAssign(res.FileLine, left, eToken.Colon, right);
			}),
			mp( Token(eToken.AtSign) + Identifier(),
				res => new SReserveIdentifier(res.GetToken("ident"), res.FileLine) ),
			mp( SimpleExpression(),
				res => new SExpression(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),
		};

		static List<SyntaxPattern> FileChains = new List<SyntaxPattern>()
		{
			
			// ::name(namelist) {{ => func }}
			mp( Token(eToken.FunctionDef) + Identifier("name") + ParameterDefList() + ArrowFuncReturn(),
				res => {
					var plist = ParseParameterDefs(res.GetTokens("params"), out _);
					SExpressionComponent exp = null;
					if(res.KeyMatched("exp"))
						exp = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("exp"));
					return new SFileFunctionDef(res.Context.Comp, res.Buffer.Key, res.FileLine, res.GetToken("name")
						, plist.Item1, plist.Item2, plist.Item3, false, plist.Item4, plist.Item5, exp);
				}),
			
			// ::.name(namelist) {{ => func }}
			mp( Token(eToken.FunctionDef) + Token(eToken.Dot) + Identifier("name") + ParameterDefList() + ArrowFuncReturn(),
				res => {
					var plist = ParseParameterDefs(res.GetTokens("params"), out _);
					SExpressionComponent exp = null;
					if(res.KeyMatched("exp"))
						exp = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("exp"));
					return new SFileFunctionDef(res.Context.Comp, res.Buffer.Key, res.FileLine, res.GetToken("name")
						, plist.Item1, plist.Item2, plist.Item3, true, plist.Item4, plist.Item5, exp);
				}),

			// {extern} global ::name(namelist) {{ => func }}
			mp( OptionalToken(eToken.Extern, "extern") + Token(eToken.Global) + Token(eToken.FunctionDef) + Identifier("name") + ParameterDefList() + ArrowFuncReturn(),
				res => {
					var plist = ParseParameterDefs(res.GetTokens("params"), out _);
					SExpressionComponent exp = null;
					if(res.KeyMatched("exp"))
						exp = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("exp"));
					return new SGlobalFunctionDef(res.FileLine, res.GetToken("name")
						, plist.Item1, plist.Item2, plist.Item3, false, plist.Item4, plist.Item5, res.KeyMatched("extern"), exp);
				}),

			// global scratch Name : value
			mp( Token(eToken.Global) + Token(eToken.Scratch) + Token(eToken.Identifier, "ident") + Token(eToken.Colon) + SimpleExpression(),
				res => {
					var toke = res.GetToken("ident");
					var value = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value"));
					return new SAssignScratch(res.FileLine, toke, eToken.Colon, value, true, false);
			}),

			// global scratch Name
			mp( Token(eToken.Global) + Token(eToken.Scratch) + Token(eToken.Identifier, "ident"),
				res => new SReserveScratch(res.GetToken("ident"), true, false) ),

			// global registry Name
			mp(  Token(eToken.Global) + Token(eToken.Registry) + Token(eToken.Identifier, "ident"),
				res => new SReserveScratch(res.GetToken("ident"), true, true) ),

			
			// scratch Name : value
			mp( Token(eToken.Scratch) + Token(eToken.Identifier, "ident") + Token(eToken.Colon) + SimpleExpression(),
				res => {
					var toke = res.GetToken("ident");
					var value = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value"));
					return new SAssignScratch(res.FileLine, toke, eToken.Colon, value, false, false);
			}),

			// scratch Name
			mp(  Token(eToken.Scratch) + Token(eToken.Identifier, "ident"),
				res => new SReserveScratch(res.GetToken("ident"), false, false) ),
			
			// registry Name : value
			mp( Token(eToken.Registry) + Token(eToken.Identifier, "ident") + Token(eToken.Colon) + SimpleExpression(),
				res => {
					var toke = res.GetToken("ident");
					var value = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value"));
					return new SAssignScratch(res.FileLine, toke, eToken.Colon, value, false, true);
			}),

			// registry Name
			mp( Token(eToken.Registry) + Token(eToken.Identifier, "ident"),
				res => new SReserveScratch(res.GetToken("ident"), false, true) ),

			mp( Token(eToken.Declare) + Anything(),
				res => new SDeclareSymbol(res.FileLine, res.GetTokens("ANYTHING"))),
			mp( Token(eToken.Require) + Anything(),
				res => new SRequireSymbol(res.FileLine, res.GetTokens("ANYTHING"))),

			
			// data Path : value
			mp( Token(eToken.Data) + Path(false, "name") + Token(eToken.Colon) + SimpleExpression(),
				res => new SData( res.FileLine,
					ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value")),
					new SStaticDeclaredPath(eStaticType.Data, res.GetTokens("name"))) ),

			// global data Path : value
			mp( Token(eToken.Global) + Token(eToken.Data) + Path(false, "name") + Token(eToken.Colon) + SimpleExpression(),
				res => new SData( res.FileLine,
					ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value")),
					new SStaticDeclaredPath(eStaticType.Data, res.GetTokens("name"), res.GetDeclaringNamespace())) ),
			
			// enum Path
			mp( Token(eToken.Enum) + Path(false, "name"),
				res => new SEnumType( res.FileLine,
					new SStaticDeclaredPath(eStaticType.EnumType, res.GetTokens("name"))) ),
			
			// global enum Path
			mp( Token(eToken.Global) + Token(eToken.Enum) + Path(false, "name"),
				res => new SEnumType( res.FileLine,
					new SStaticDeclaredPath(eStaticType.EnumValue, res.GetTokens("name"), res.GetDeclaringNamespace()),
					true) ),

			// $path : value
			mp( Path(true) + Token(eToken.Colon) + SimpleExpression(),
				res => new SConst(res.FileLine
					, new SStaticDeclaredPath(eStaticType.Constant, res.GetCleanedPath())
					, ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value")))
				),
		};

		static List<SyntaxPattern> LibraryChain = new List<SyntaxPattern>()
		{
			// {extern} ::name(namelist) {{ => func }}
			mp(OptionalToken(eToken.Extern, "extern") + Token(eToken.FunctionDef) + Identifier("name") + ParameterDefList() + ArrowFuncReturn(),
				res => {
					var plist = ParseParameterDefs(res.GetTokens("params"), out _);
					SExpressionComponent exp = null;
					if(res.KeyMatched("exp"))
						exp = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("exp"));
					return new SLibFunctionDef(res.Context.Comp, res.FileLine
						, new SStaticDeclaredPath(eStaticType.Function, res.GetTokens("name"), res.GetDeclaringNamespace())
						, res.GetToken("name"), plist.Item1, plist.Item2, plist.Item3, false, plist.Item4, plist.Item5, res.KeyMatched("extern"), exp);
				}),
			
			// {extern} ::.name(namelist) {{ => func }}
			mp(OptionalToken(eToken.Extern, "extern") + Token(eToken.FunctionDef) + Token(eToken.Dot) + Identifier("name") + ParameterDefList() + ArrowFuncReturn(),
				res => {
					var plist = ParseParameterDefs(res.GetTokens("params"), out _);
					SExpressionComponent exp = null;
					if(res.KeyMatched("exp"))
						exp = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("exp"));
					return new SLibFunctionDef(res.Context.Comp, res.FileLine
						, new SStaticDeclaredPath(eStaticType.Function, res.GetTokens("name"), res.GetDeclaringNamespace())
						, res.GetToken("name"), plist.Item1, plist.Item2, plist.Item3, true, plist.Item4, plist.Item5, res.KeyMatched("extern"), exp);
				}),
			
			// data Path : value
			mp( Token(eToken.Data) + Path(false, "name") + Token(eToken.Colon) + SimpleExpression(),
				res => new SData( res.FileLine,
					ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value")),
					new SStaticDeclaredPath(eStaticType.Data, res.GetTokens("name"), res.GetDeclaringNamespace())) ),
			
			// data : value
			mp( Token(eToken.Data) + Token(eToken.Colon) + SimpleExpression(),
				res => new SData(res.FileLine,
					ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value"))) ),

			// enum Path
			mp( Token(eToken.Enum) + Path(false, "name"),
				res => new SEnumType( res.FileLine,
					new SStaticDeclaredPath(eStaticType.EnumValue, res.GetTokens("name"), res.GetDeclaringNamespace())) ),

			// $path : value
			mp( Path(true) + Token(eToken.Colon) + SimpleExpression(),
				res => new SConst(res.FileLine
					, new SStaticDeclaredPath(eStaticType.Constant, res.GetCleanedPath(), res.GetDeclaringNamespace())
					, ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value")))
				),
			
			// extern $path : value
			mp( Token(eToken.Extern) + Path(true) + Token(eToken.Colon) + SimpleExpression(),
				res => new SConst(res.FileLine
					, new SStaticDeclaredPath(eStaticType.External, res.GetCleanedPath(), res.GetDeclaringNamespace())
					, ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value")))
				),

			// library path
			mp( Token(eToken.Library) + Path(false),
				res => new SLibrary(res.FileLine
					, new SStaticDeclaredPath(eStaticType.Library, res.GetCleanedPath(), res.GetDeclaringNamespace()))
				),
		};

		static List<SyntaxPattern> SwitchExpressionCaseChain = new List<SyntaxPattern>()
		{
			mp( Token(eToken.Else) + Token(eToken.Colon) + SimpleExpression(),
				res => new SSwitchExpressionElse(res.FileLine,
					ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value"))) ),

			mp( SimpleExpression("test", eToken.Colon) + Token(eToken.Colon) + SimpleExpression(),
				res => new SSwitchExpressionCase(res.FileLine,
					ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("test")),
					ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("value"))) ),
		};

		static List<SyntaxPattern> SwitchCaseChain = new List<SyntaxPattern>()
		{
			mp( Token(eToken.Else),
				res => new SSwitchCase(res.FileLine, null) ),

			mp( Token(eToken.Case) + SimpleExpression("test"),
				res => {
					var cs = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("test"));
					if(cs == null) throw new ParserException("could not evaluate case", res.GetTokens("test")[0]);
					return new SSwitchCase(res.FileLine, cs);
				}),
		};

		static List<SyntaxPattern> SwitchIfChain = new List<SyntaxPattern>()
		{
			mp( Token(eToken.Else),
				res => new SSwitchCase(res.FileLine, null) ),

			mp( SimpleExpression("test"),
				res => {
					var cs = ExpressionParser.ParseExpressionComponent(res.Context,res.GetTokens("test"));
					if(cs == null) throw new ParserException("could not evaluate case", res.GetTokens("test")[0]);
					return new SSwitchCase(res.FileLine,cs);
				}),
		};


		static List<SyntaxPattern> DimChildrenChain = new List<SyntaxPattern>()
		{
			// mixin $path(params)
			mp(Token(eToken.Mixin) + Path(true) + ParameterList(),
				res => {
					var plist = ExpressionParser.ParseParameterList(res.Context, res.GetTokens("params"));
					return new SMixin(res.FileLine, res.GetCleanedPath(), res.GetUsingScope(), plist);
				}),

			// key : value
			mp(Identifier("key") + Token(eToken.Colon) + SimpleExpression(),
				res => new SDimLiteralKeyIdent(res.FileLine, new SIdentifier(res.GetToken("key")),
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),

			// ::.name(params) {{ => func }}
			mp( Token(eToken.FunctionDef) + Token(eToken.Dot) + Identifier("name") + ParameterDefList() + ArrowFuncReturn(),
				res => {
					var plist = ParseParameterDefs(res.GetTokens("params"), out _);
					SExpressionComponent exp = null;
					if(res.KeyMatched("exp"))
						exp = ExpressionParser.ParseExpressionComponent(res.Context, res.GetTokens("exp"));
					return new SDimMethod(res.FileLine,  res.GetToken("name")
						, plist.Item1, plist.Item2, plist.Item3, plist.Item4, plist.Item5, exp);
				}),

			// "key" : value
			mp( SimpleExpression("key", eToken.Colon) + Token(eToken.Colon) + SimpleExpression(),
				res => new SKeyValuePair(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("key")),
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),

			// value
			mp( SimpleExpression("value"),
				res => new SDimAutoArrayNode(res.FileLine,
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),
		};

		static List<SyntaxPattern> EnumChildrenChain = new List<SyntaxPattern>()
		{

			// key : value
			mp(Identifier("key") + Token(eToken.Colon) + SimpleExpression(),
				res => new SEnumValue(res.FileLine, res.GetDeclaringNamespace()
					, new SIdentifier(res.GetToken("key")),
					ExpressionParser.ParseExpression(res.Context, res.GetTokens("value"))) ),

			// value
			mp( Identifier("key"),
				res => new SEnumValue(res.FileLine, res.GetDeclaringNamespace()
					, new SIdentifier(res.GetToken("key")) )),
		};

		#endregion

		#region chain helpers
		static ChainList Token(eToken token, string key = "")
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				var t = tokens[begin].Token.Type;
				bool r = (t == token);
				return new MatchResult(r, begin, begin, key);
			});
		}
		static ChainList AnyOf(string key, params eToken[] possible)
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				var t = tokens[begin].Token.Type;
				bool r = possible.Contains(t);
				return new MatchResult(r, begin, begin, key);
			});
		}
		static ChainList OptionalToken(eToken token, string key = "")
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				var t = tokens[begin].Token.Type;
				if (t == token)
					return new MatchResult(true, begin, begin, key);
				else
					return new MatchResult(true, begin, begin - 1);
			});
		}
		static ChainList Identifier(string key = "ident")
			=> Token(eToken.Identifier, key);
		static ChainList LiteralNumber(string key = "num")
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				var t = tokens[begin].Token.Type;
				if (t == eToken.Subtract && tokens.Length > begin + 1 && tokens[begin + 1].Token.Type == eToken.LiteralNumber)
					return new MatchResult(true, begin, begin + 1, key);
				return new MatchResult(t == eToken.LiteralNumber, begin, begin, key);
			});
		}

		// validates the next token is the one specified (prevent recursion)
		static ChainList AntiPeek(eToken token)
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				var t = tokens[begin].Token.Type;
				bool r = (t != token);
				return new MatchResult(r, begin, begin - 1);
			});
		}
		static ChainList Nothing()
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				var t = tokens[begin].Token.Type;
				bool r = (t == eToken.BackSlash);
				return new MatchResult(r, begin, begin - 1);
			});
		}


		//this is a naive parse for speed. full parse done after line syntax is established
		static ChainList SimpleExpression(string key = "value", params eToken[] next)
			=> _Expression(key, false, next);
		static ChainList ExpressionList(string key = "list", params eToken[] next)
			=> _Expression(key, true, next);

		static ChainList _Expression(string key, bool list, params eToken[] next)
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				var end = ScanExpression(tokens, begin, list, next);
				return new MatchResult(end > begin, begin, end - 1, key);
			});
		}
		static ChainList ArrowFuncReturn(string key = "exp", bool optional = true)
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				var t = tokens[begin].Token.Type;
				if (t != eToken.Arrow)
					return new MatchResult(false, begin, begin, key);
				var end = ScanExpression(tokens, begin + 1, false);
				return new MatchResult(end > begin, begin + 1, end - 1, key);
			}, optional);
		}

		private static int ScanExpression(RelativeTokenReference[] tokens, int begin, bool list, params eToken[] next)
		{
			// this doesn't quite work :( - that function doesn't handle the next param
			// return ExpressionParser.FindEnd(tokens, begin, 0, false, false);
			int i;
			Stack<eToken> closing = new Stack<eToken>();
			Dictionary<eToken, eToken> pairedTokens = new Dictionary<eToken, eToken>()
				{
					{eToken.LeftParen,  eToken.RightParen },
					{eToken.LeftBracket,  eToken.RightBracket },
					{eToken.LeftBrace,  eToken.RightBrace },
					{eToken.OneLiner, eToken.OneLiner },
				};
			for (i = begin; i < tokens.Length; i++)
			{
				var t = tokens[i].Token;
				if (next != null && next.Contains(t.Type) && closing.Count == 0)
					break;
				else if (!list && t.Type == eToken.Comma && closing.Count == 0)
					break;
				else if (t.Type == eToken.BackSlash)
					break;
				else if (t.Type == eToken.SemiColon)
					break;
				else if (closing.Count > 0 && closing.Peek() == t.Type)
					closing.Pop();
				else if (pairedTokens.ContainsKey(t.Type))
					closing.Push(pairedTokens[t.Type]);
			}
			return i;
		}

		// includes inputs and returns in the span!
		static ChainList ParameterDefList(string key = "params")
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				int end;
				if (tokens[begin].Token.Type != eToken.LeftParen)
					return new MatchResult(false);
				bool test = (SplitParameterDefs(tokens, begin, out end) != null);
				return new MatchResult(test, begin, end, key);
			});
		}
		static ChainList ParameterList(string key = "params")
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				int end;
				if (tokens[begin].Token.Type != eToken.LeftParen)
					return new MatchResult(false);
				bool test = (SplitParameters(tokens, begin, out end) != null);
				return new MatchResult(test, begin, end, key);
			});
		}
		static List<RelativeTokenReference[]> SplitParameters(RelativeTokenReference[] tokens, int begin, out int end)
		{
			Debug.Assert(tokens[begin].Token.Type == eToken.LeftParen);
			List<RelativeTokenReference[]> par = new List<RelativeTokenReference[]>();
			for (end = begin + 1; end < tokens.Length; end++)
			{
				var next = ScanExpression(tokens, end, true, eToken.RightParen);
				if (next == end && par.Count == 0) break; // empty ()
				var curr = util.RangeSubset(tokens, end, next - end);
				par.Add(curr.ToArray());
				end = next;
				if (end >= tokens.Length)
					break;
				BaseToken t = tokens[end].Token;
				if (t.Type == eToken.RightParen)
					break;
			}
			if (end >= tokens.Length) return null;
			return par;
		}

		static Tuple<List<RelativeTokenReference[]>, List<RelativeTokenReference[]>, bool, bool, bool>
			SplitParameterDefs(RelativeTokenReference[] tokens, int begin, out int end)
		{
			Debug.Assert(tokens[begin].Token.Type == eToken.LeftParen);
			var idxParen = Array.FindIndex(tokens, begin + 1, t => t.Token.Type == eToken.RightParen);
			if (idxParen < 0) { end = begin; return null; }
			end = idxParen;
			var fullArray = util.RangeSubset(tokens, begin + 1, idxParen - begin - 1);

			var idxArrow = Array.FindIndex(fullArray, t => t.Token.Type == eToken.Arrow);
			var inputs = new List<RelativeTokenReference[]>();
			var outputs = new List<RelativeTokenReference[]>();
			bool isAsync = false;
			bool isYield = false;
			bool isThrow = false;
			if (idxArrow != 0)
			{
				var rawIns = (idxArrow > 0) ? fullArray.RangeFront(idxArrow) : fullArray;
				inputs = rawIns.SplitArr(t => t.Token.Type == eToken.Comma);
				//sanity check
				for (int i = 0; i < inputs.Count; i++)
				{
					var curr = inputs[i];
					if (!SplitParameter(curr, out _, out _, out _, out _, i == inputs.Count - 1))
						return null;
				}
			}

			if (idxArrow >= 0)
			{
				var rawOut = fullArray.RangeRemainder(idxArrow + 1);
				if (rawOut.Length > 0 && rawOut[0].Token.Type == eToken.Async)
				{
					rawOut = rawOut.RangeRemainder(1);
					isAsync = true;
				}
				if (rawOut.Length > 0 && rawOut[0].Token.Type == eToken.Throw)
				{
					rawOut = rawOut.RangeRemainder(1);
					isThrow = true;
				}
				else if (rawOut.Length > 0 && rawOut[0].Token.Type == eToken.Yield)
				{
					rawOut = rawOut.RangeRemainder(1);
					isYield = true;
				}
				if (rawOut.Length > 0)
				{
					outputs = rawOut.SplitArr(t => t.Token.Type == eToken.Comma);
					for (int i = 0; i < outputs.Count; i++)
					{
						var curr = outputs[i];
						if (curr.Length == 1 && curr[0].Token.Type == eToken.Identifier) continue;
						return null;
					}
				}
			}
			return new Tuple<List<RelativeTokenReference[]>, List<RelativeTokenReference[]>, bool, bool, bool>(inputs, outputs, isYield, isAsync, isThrow);
		}

		static bool SplitParameter(RelativeTokenReference[] par, out RelativeTokenReference ident, out bool isMulti, out bool isOptional, out bool isOwnedMemory, bool canBeMulti = true)
		{
			isMulti = false;
			isOptional = false;
			isOwnedMemory = false;
			ident = new RelativeTokenReference();

			var idx = 0;
			bool IsDone() => idx >= par.Length;
			eToken Peek() => par[idx].Token.Type;
			RelativeTokenReference Pop() => par[idx++];

			if (IsDone()) return false;
			if (Peek() == eToken.QuestionMark)
			{
				Pop();
				isOptional = true;
			}
			if (Peek() == eToken.And)
			{
				Pop();
				isOwnedMemory = true;
			}

			if (Peek() != eToken.Identifier)
				return false;
			ident = Pop();

			if (IsDone()) return true;

			if (!canBeMulti) return false;
			if (Peek() != eToken.LeftBracket)
				return false;
			Pop();
			if (Peek() != eToken.RightBracket)
				return false;
			Pop();
			isMulti = true;

			if (!IsDone()) return false;
			return true;
		}

		static ChainList Anything()
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				return new MatchResult(true, begin, tokens.Length - 1, "ANYTHING");
			});
		}
		static ChainList Path(bool expectStatic, string key = "path")
		{
			return new SingleMatch((context, tokens, begin) =>
			{
				if (expectStatic && tokens[begin].Token.Type != eToken.StaticIdentifier)
					return new MatchResult(false);
				if (!expectStatic && tokens[begin].Token.Type != eToken.Identifier)
					return new MatchResult(false);
				var end = begin;
				for (int i = begin + 1; i < tokens.Length - 1; i += 2)
				{
					if (tokens[i].Token.Type != eToken.Dot) break;
					if (tokens[i + 1].Token.Type != eToken.Identifier) break;
					end = i + 1;
				}
				return new MatchResult(true, begin, end, key);
			});
		}
		#endregion

		private static bool TryParse(ParseContext context, RelativeTokenReference[] tokens, out int usedTokens, List<SyntaxPattern> possible, out SyntaxNode node)
		{
			int lastToken = 0;
			var pairs = possible.ToList();
			for (int i = 0; i < pairs.Count; i++)
			{
				var pair = pairs[i];
				usedTokens = 0;
				ChainList list = pair.Match;
				int chain = 0;
				bool fail = false;
				List<MatchResult> results = new List<MatchResult>();
				for (chain = 0; chain < list.Count && usedTokens < tokens.Length; chain++)
				{
					var res = list[chain].Handle.Invoke(context, tokens, usedTokens);
					if (!res.Match && !list[chain].Optional) { fail = true; break; }
					results.Add(res);
					if (res.Match)
					{
						if (res.End > lastToken) lastToken = res.End;
						usedTokens = res.End + 1;
					}
				}
				if (!fail)
					for (; chain < list.Count; chain++)
					{
						if (list[chain].Optional)
							results.Add(new MatchResult(false));
						else
							fail = true;
					}
				if (!fail)
				{
					node = pair.Generator.Invoke(new Result(context, results, tokens));
					return true;
				}
			}
			usedTokens = tokens.Length;
			node = null;
			return false;
		}

		#region structures
		internal static bool TryParseFile(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return TryParse(context, currLine, out usedTokens, FileChains, out node);
		}
		internal static bool TryParseLibrary(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return TryParse(context, currLine, out usedTokens, LibraryChain, out node);
		}
		#endregion

		#region statements

		static List<SStatement> ParseMultiStatement(ParseContext context, RelativeTokenReference[] currLine)
		{
			List<SStatement> list = new List<SStatement>();
			RelativeTokenReference[] copy = util.RangeRemainder(currLine, 0);
			while (copy.Length > 0)
			{
				TryParseStatement(context, copy, out var node, out var used);
				list.Add(node as SStatement);
				if (used == 0) used++;
				if (used >= copy.Length) break;
				if (copy[used].Token.Type != eToken.SemiColon)
					context.Errors.LogError("Expected semicolon", context.FileLine, copy[used]);
				else used++;
				copy = util.RangeRemainder(copy, used);
			}
			return list;
		}

		// expects only one single statement
		internal static SStatement ParseStatement(ParseContext context, RelativeTokenReference[] currLine)
		{
			TryParseStatement(context, currLine, out var node, out var usedTokens);
			if (usedTokens != currLine.Length) return null;
			return node as SStatement;
		}

		internal static bool TryParseStatement(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return TryParse(context, currLine, out usedTokens, StatementChains, out node);
		}

		#region switches
		internal static SyntaxNode ParseSwitchExpressionCase(ParseContext context, RelativeTokenReference[] currLine)
		{
			TryParseSwitchExpressionCase(context, currLine, out var node, out var usedTokens);
			if (usedTokens != currLine.Length) return null;
			return node;
		}
		internal static bool TryParseSwitchExpressionCase(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return TryParse(context, currLine, out usedTokens, SwitchExpressionCaseChain, out node);
		}
		internal static SyntaxNode ParseSwitchCase(ParseContext context, RelativeTokenReference[] currLine)
		{
			TryParseSwitchCase(context, currLine, out var node, out var usedTokens);
			if (usedTokens != currLine.Length) return null;
			return node;
		}
		internal static bool TryParseSwitchCase(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return TryParse(context, currLine, out usedTokens, SwitchCaseChain, out node);
		}
		internal static bool TryParseSwitchIf(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return TryParse(context, currLine, out usedTokens, SwitchIfChain, out node);
		}
		#endregion

		internal static bool TryParseDimChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return TryParse(context, currLine, out usedTokens, DimChildrenChain, out node);
		}
		internal static SDimElement ParseDimChild(ParseContext context, RelativeTokenReference[] currLine, out int usedTokens)
		{
			TryParseDimChild(context, currLine, out var node, out usedTokens);
			return node as SDimElement;
		}

		internal static bool TryParseEnumChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return TryParse(context, currLine, out usedTokens, EnumChildrenChain, out node);
		}
		internal static SEnumValue ParseEnumChild(ParseContext context, RelativeTokenReference[] currLine, out int usedTokens)
		{
			TryParseEnumChild(context, currLine, out var node, out usedTokens);
			return node as SEnumValue;
		}


		internal static Tuple<List<SParameter>, List<SIdentifier>, bool, bool, bool> ParseParameterDefs(RelativeTokenReference[] currLine, out int usedTokens)
		{
			var split = SplitParameterDefs(currLine, 0, out usedTokens);
			List<SParameter> inputs = new List<SParameter>();
			foreach (var arr in split.Item1)
			{
				SplitParameter(arr, out var token, out var isMulti, out var isOpt, out var isOwned);
				if (isMulti) inputs.Add(new SMultiParameter(token.Token.Token, isOpt, isOwned));
				else inputs.Add(new SParameter(token.Token.Token, isOpt, isOwned));
			}
			List<SIdentifier> outputs = new List<SIdentifier>();
			foreach (var arr in split.Item2)
				outputs.Add(new SIdentifier(arr[0])); // assumes returns have no modifier tokens!
			return new Tuple<List<SParameter>, List<SIdentifier>, bool, bool, bool>(inputs, outputs, split.Item3, split.Item4, split.Item5);
		}

		#endregion
	}
}
