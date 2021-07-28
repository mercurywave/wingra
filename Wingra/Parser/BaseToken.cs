using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	public enum eToken
	{
		Unknown, // unexpected token
		BeginString, EndString, TextData,  // "
		LiteralNumber, LiteralString,
		LeftParen, RightParen,
		LeftBracket, RightBracket,
		LeftBrace, RightBrace,
		Comma,
		Add, Subtract, Multiply, Divide,
		EqualSign, NotEquals,
		Greater, Less, EqGreater, EqLess,
		And, Or, Not,
		True, False,
		Null,
		Has, Copy, Free,
		Identifier, // name
		StaticIdentifier, // $ident
		GlobalIdentifier, // ^ident
		CommentBegin, Comment, // for now, include in lex? - useful for syntax hilighting
		If, Else, Switch, Select, Case,
		For, While, Until,
		Break, Continue,
		Return, Yield, Quit,
		To, By, In, At, Of, // for loop
		Dot, This, New, Dim, AtSign,
		Using,
		Template, Data, Library, Enum,
		Trap, Throw, Try, Catch, Avow,
		Colon, SemiColon, BackSlash, Dollar,
		FunctionDef,
		Arrow, // =>
		QuestionMark,
		DotQuestion, // .? - null if the property doesn't exist (useful for retrieval without 'has' check)
		QuestionDot, // ?. - halt if left side is null (useful for method chaining)
		Meh, // ~
		Defer,
		Macro, MacroDef, // #ident, #def
		Async, Await, Arun,
		BootStrap, // #bootstrap
		Declare, Require, // #declares, #requires
		LineContinuation,
		Import, Namespace, Mixin, Global, Scratch, Registry, Extern,
		OneLiner, Lambda, //`
	}
	public struct BaseToken
	{
		public eToken Type;
		public string Token;
		public int LineOffset;

		public BaseToken(eToken type, string token, int offset)
		{
			Type = type;
			Token = token;
			LineOffset = offset;
		}
		public int Length => Token.Length;

		public bool IsReserved() => IsTokenReserved(Type);

		public static bool IsTokenReserved(eToken type)
		{
			switch (type)
			{
				case eToken.If:
				case eToken.Else:
				case eToken.Switch:
				case eToken.Select:
				case eToken.Case:
				case eToken.For:
				case eToken.To:
				case eToken.By:
				case eToken.In:
				case eToken.At:
				case eToken.Of:
				case eToken.True:
				case eToken.False:
				case eToken.Null:
				case eToken.While:
				case eToken.Until:
				case eToken.Break:
				case eToken.Continue:
				case eToken.Return:
				case eToken.Yield:
				case eToken.Quit:
				case eToken.Template:
				case eToken.Using:
				case eToken.Data:
				case eToken.New:
				case eToken.Dim:
				case eToken.Arrow:
				case eToken.Library:
				case eToken.Enum:
				case eToken.Trap:
				case eToken.Catch:
				case eToken.Try:
				case eToken.Throw:
				case eToken.Avow:
				case eToken.Has:
				case eToken.Copy:
				case eToken.Free:
				case eToken.This:
				case eToken.Import:
				case eToken.Global:
				case eToken.Scratch:
				case eToken.Registry:
				case eToken.Namespace:
				//case eToken.Test:
				//case eToken.Assert:
				case eToken.Mixin:
				case eToken.Lambda:
				case eToken.TextData:
				case eToken.Async:
				case eToken.Await:
				case eToken.Arun:
				case eToken.Defer:
				case eToken.Extern:
					return true;
			}
			return false;
		}

		public bool IsDirective() => IsTokenDirective(Type);
		public static bool IsTokenDirective(eToken type)
		{
			switch (type)
			{
				case eToken.Macro:
					return true;
			}
			return false;
		}

		public static bool DoesTokenHaveSymbol(eToken type)
		{
			switch (type)
			{
				case eToken.BeginString:
				case eToken.EndString:
				case eToken.Comment:
				case eToken.Identifier:
				case eToken.StaticIdentifier:
				case eToken.GlobalIdentifier:
				case eToken.LiteralNumber:
				case eToken.LiteralString:
				case eToken.Macro:
				case eToken.MacroDef:
				case eToken.BootStrap:
				case eToken.Unknown:
					return false;
			}
			return true;
		}

		public bool IsTokenBuiltInType() => IsTokenBuiltInType(Type);
		public static bool IsTokenBuiltInType(eToken type)
		{
			switch (type)
			{
				case eToken.Null:
				case eToken.True:
				case eToken.False:
					return true;
			}
			return false;
		}

		public bool DoesSymbolImplyIndent() => DoesSymbolImplyIndent(Type);
		public static bool DoesSymbolImplyIndent(eToken type) //if the first symbol is this, the next line should probably be indented
		{
			switch (type)
			{
				case eToken.If:
				case eToken.Else:
				case eToken.Switch:
				case eToken.For:
				case eToken.While:
				case eToken.Template:
				case eToken.Library:
				//case eToken.Data:
				case eToken.Enum:
				case eToken.Trap:
				case eToken.FunctionDef:
				case eToken.Dim:
				case eToken.MacroDef:
				case eToken.Lambda:
				case eToken.TextData:
				case eToken.Case:
				case eToken.Defer:
					return true;
			}
			return false;
		}

		public bool IsComment()
		{
			return (Type == eToken.Comment || Type == eToken.CommentBegin);
		}

		public bool IsLiteral()
		{
			return (Type == eToken.LiteralNumber || Type == eToken.LiteralString);
		}

		// as opposed to metadata / comments
		public bool IsRealCode() => IsRealCode(Type);
		public static bool IsRealCode(eToken type)
		{
			switch (type)
			{
				case eToken.Comment:
				case eToken.CommentBegin:
				case eToken.BootStrap:
					return false;
			}
			return true;
		}

		public override string ToString()
		{
			return "Base: " + Token + " " + Type.ToString();
		}

		public bool IsTextPositionInToken(int x) => (x >= LineOffset && x <= LineOffset + Token.Length);

		public static IEnumerable<T> EnumOptions<T>()
		{
			return Enum.GetValues(typeof(T)).Cast<T>();
		}
		public static IEnumerable<eToken> MappedSymbols()
		{
			foreach (eToken tok in AllTokens())
			{
				if (DoesTokenHaveSymbol(tok))
					yield return tok;
			}
		}
		public static IEnumerable<eToken> AllTokens() => EnumOptions<eToken>();

		public static int TokenPriority(eToken token, int defVal = 99)
		{
			if (IsUnary(token))
				return UnaryPriority(token);
			if (IsOperand(token))
				return OpPriority(token);
			return defVal;
		}

		public bool IsUnary() => IsUnary(Type);
		public static bool IsUnary(eToken token)
		{
			switch (token)
			{
				case eToken.AtSign: // these two are sort of a hack
				case eToken.Global:

				case eToken.Subtract:
				case eToken.Meh:
				//case eToken.New:
				case eToken.Not:
				case eToken.Dim:
				case eToken.Add:
				case eToken.Copy:
				case eToken.Async:
				case eToken.Await:
				case eToken.Arun:
					return true;
				default: return false;
			}
		}
		public static int UnaryPriority(eToken token)
		{
			switch (token)
			{
				case eToken.Add:
				case eToken.Subtract:
				case eToken.Not:
				case eToken.Meh:
				//case eToken.New:
				case eToken.Dim:
				case eToken.Copy:
				case eToken.Await:
				case eToken.Arun:
					return 5;
				case eToken.AtSign:
				case eToken.Global:
					return 7;
				default: throw new NotImplementedException("unexpected unary");
			}
		}
		public static int OpPriority(eToken token)
		{
			switch (token)
			{
				case eToken.Or:
				case eToken.And:
					return 1;
				case eToken.EqualSign:
				case eToken.NotEquals:
				case eToken.EqLess:
				case eToken.EqGreater:
				case eToken.Less:
				case eToken.Greater:
				case eToken.Has:
					return 2;
				case eToken.Add:
				case eToken.Subtract:
					return 3;
				case eToken.Multiply:
				case eToken.Divide:
					return 4;
				case eToken.QuestionMark:
					return 5;
				case eToken.LeftBracket: // function / array access
				case eToken.LeftParen:
				case eToken.Dot:
				case eToken.DotQuestion:
				case eToken.QuestionDot:
					return 6;
				case eToken.LeftBrace:
				case eToken.RightBrace:
					return 7;
				default: throw new NotImplementedException("unexpected operator");
			}
		}

		public bool IsOperand() => IsOperand(Type);
		public static bool IsOperand(eToken token)
		{
			switch (token)
			{
				case eToken.And:
				case eToken.Or:
				case eToken.EqualSign:
				case eToken.NotEquals:
				case eToken.EqLess:
				case eToken.EqGreater:
				case eToken.Less:
				case eToken.Greater:
				case eToken.Add:
				case eToken.Subtract:
				case eToken.Multiply:
				case eToken.Divide:
				case eToken.LeftBracket: // function / array access
				case eToken.LeftParen:
				case eToken.LeftBrace:
				case eToken.RightBrace:
				case eToken.Dot:
				case eToken.DotQuestion:
				case eToken.QuestionDot:
				case eToken.QuestionMark:
				case eToken.Has:
					return true;
				default: return false;
			}
		}

		public bool DoesShortCircuit() => DoesShortCircuit(Type);
		public static bool DoesShortCircuit(eToken token)
		{
			switch (token)
			{
				case eToken.And:
				case eToken.Or:
				case eToken.QuestionMark:
					return true;
				default: return false;
			}
		}

		public static bool IsOperandOrUnary(eToken token) => IsOperand(token) || IsUnary(token);
	}

	public struct RelativeTokenReference
	{
		public BaseToken Token;
		public int SubLine;
		public RelativeTokenReference(BaseToken token, int subLine)
		{
			Token = token;
			SubLine = subLine;
		}

		public override string ToString()
		{
			return "rel token: " + Token.ToString();
		}

		public static RelativeTokenReference FromList(RelativeTokenReference[] list)
		{
			if (list == null || list.Length == 0) return new RelativeTokenReference();
			return list[0];
		}
	}
}
