using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	static class ExpressionParser
	{

		internal static SExpressionComponent ParseExpression(ParseContext context, RelativeTokenReference[] currLine)
		{
			TryParseExpression(context, currLine, out var node, out _);
			return node;
		}
		internal static bool TryParseExpression(ParseContext context, RelativeTokenReference[] currLine, out SExpressionComponent node, out int usedTokens, params eToken[] halt)
		{
			node = null;
			usedTokens = 0;
			List<SExpressionComponent> components = new List<SExpressionComponent>();

			while (usedTokens < currLine.Length)
			{
				if (halt.Contains(currLine[usedTokens].Token.Type)) break;
				var subset = util.RangeRemainder(currLine, usedTokens);
				if (TryParseExpressionComponent(context, subset, out var sExp, out int sUsed, halt))
				{
					components.Add(sExp);
					usedTokens += sUsed;
				}
				else
				{
					var nextToke = currLine[usedTokens];
					if (halt.Contains(nextToke.Token.Type)) break;
					if (!BaseToken.IsOperandOrUnary(nextToke.Token.Type))
						throw new ParserException("unexpected operator " + nextToke.Token.Token, nextToke);

					usedTokens++;
					if (nextToke.Token.Type == eToken.AtSign)
					{
						// it would be clumsy to try and handle this later with the other Unary ops
						if (usedTokens + 1 > currLine.Length)
							components.Add(new SIgnoredVariable(nextToke));
						else
						{
							var ident = currLine[usedTokens];
							if (ident.Token.Type != eToken.Identifier)
								components.Add(new SIgnoredVariable(nextToke));
							else
							{
								usedTokens++;
								components.Add(new SReserveIdentifierExp(ident));
							}
						}
					}
					else components.Add(new SOperand(nextToke.Token.Type));

					if (nextToke.Token.Type == eToken.LeftBracket)
					{
						if (!TryParseExpressionList(context, util.RangeRemainder(currLine, usedTokens), out var list, out var listUsed, eToken.RightBracket))
							throw new ParserException("key access must have a key");
						usedTokens += listUsed + 1;
						if (currLine[usedTokens - 1].Token.Type != eToken.RightBracket)
							throw new ParserException("expected \"]\"");
						components.Add(new SParamList(list));
					}
				}
			}

			components.Reverse();
			Stack<SExpressionComponent> stack = new Stack<SExpressionComponent>(components);

			node = CollapseExpression(stack);
			if (node == null) throw new ParserException("failed to collapse expression");
			return true;
		}

		internal static SExpressionComponent ParseExpressionComponent(ParseContext context, RelativeTokenReference[] currLine, params eToken[] halt)
		{
			if (!TryParseExpression(context, currLine, out var node, out var used, halt))
				return null;
			return node;
		}
		static bool TryParseExpressionComponent(ParseContext context, RelativeTokenReference[] currLine, out SExpressionComponent node, out int usedTokens, params eToken[] halt)
		{
			node = null;
			usedTokens = 0;
			if (currLine.Length < 1) return false;
			var lead = currLine[0];
			if (halt.Contains(lead.Token.Type)) return true;
			switch (lead.Token.Type)
			{
				case eToken.LiteralNumber:
					usedTokens = 1;
					node = new SLiteralNumber(new RelativeTokenReference[] { lead });
					return true;
				case eToken.BeginString:
					{
						if (currLine.Length >= 2 && currLine[1].Token.Type == eToken.EndString)
						{
							// empty string
							usedTokens = 2;
							node = new SLiteralString(null);
							return true;
						}
						if (currLine.Length < 3
							|| currLine[1].Token.Type != eToken.LiteralString
							|| currLine[2].Token.Type != eToken.EndString)
							throw new ParserException("missing \"", lead);
						usedTokens = 3;
						node = new SLiteralString(currLine[1]);
						return true;
					}
				case eToken.LeftParen:
					{
						if (currLine.Length < 3) return false;
						if (TryParseExpression(context, util.RangeRemainder(currLine, 1), out var expComp, out int dist, eToken.RightParen))
						{
							if (dist + usedTokens >= currLine.Length || currLine[1 + dist].Token.Type != eToken.RightParen)
								throw new ParserException("missing \")\"", lead);
							usedTokens = dist + 2; // includes ( and )
							node = expComp;
							return true;
						}
						return false;
					}
				case eToken.This:
					usedTokens = 1;
					node = new SIdentifier(lead);
					return true;
				case eToken.Dollar:
					{
						if (currLine.Length > 3 && currLine[1].Token.Type == eToken.LeftParen)
						{
							if (halt.Contains(currLine[1].Token.Type)) return true;
							usedTokens = 2;
							if (TryParseExpressionList(context, util.RangeRemainder(currLine, 2), out var expParams, out int dist, eToken.RightParen))
							{
								if (dist + usedTokens >= currLine.Length || currLine[dist + usedTokens].Token.Type != eToken.RightParen)
									throw new ParserException("indirection missing \")\"", lead);
								usedTokens += dist + 1;
								node = new SExecute(expParams);
								return true;
							}
						}
						throw new ParserException("expected complete function call", lead);
					}
				case eToken.Identifier:
					usedTokens = 1;
					node = new SIdentifier(lead);
					if (currLine.Length > 2 && currLine[1].Token.Type == eToken.LeftParen)
					{
						if (halt.Contains(currLine[1].Token.Type)) return true;
						if (currLine[2].Token.Type == eToken.RightParen)
						{
							usedTokens += 2;
							node = new SFunctionCall(node as SIdentifier, null);
						}
						else if (TryParseExpressionList(context, util.RangeRemainder(currLine, 2), out var expParams, out int dist, eToken.RightParen))
						{
							if (dist + usedTokens + 1 >= currLine.Length || currLine[1 + dist + usedTokens].Token.Type != eToken.RightParen)
								throw new ParserException("function missing \")\"", lead);
							usedTokens += dist + 2;
							node = new SFunctionCall(node as SIdentifier, expParams);
						}
					}
					return true;
				case eToken.StaticIdentifier:
					{
						List<RelativeTokenReference> chain = new List<RelativeTokenReference>();
						chain.Add(currLine[0]);
						for (int i = 1; i < currLine.Length - 1; i += 2)
						{
							if (currLine[i].Token.Type != eToken.Dot) break;
							if (currLine[i + 1].Token.Type != eToken.Identifier) break;
							chain.Add(currLine[i + 1]);
						}
						usedTokens = chain.Count * 2 - 1;
						if (currLine.Length >= usedTokens + 2 && currLine[usedTokens].Token.Type == eToken.LeftParen)
						{
							usedTokens++; // (
							if (currLine[usedTokens].Token.Type == eToken.RightParen)
							{
								usedTokens++;
								node = new SStaticFunctionCall(chain.ToArray(), context.Scope.GetUsingNamespaces(), null);
								return true;
							}
							if (TryParseExpressionList(context, util.RangeRemainder(currLine, usedTokens), out var expParams, out int dist, eToken.RightParen))
							{
								if (dist + usedTokens >= currLine.Length || currLine[dist + usedTokens].Token.Type != eToken.RightParen)
									throw new ParserException("function call missing \")\"", lead);
								usedTokens += dist + 1;
								node = new SStaticFunctionCall(chain.ToArray(), context.Scope.GetUsingNamespaces(), expParams);
								return true;
							}
						}
						else
							node = new SStaticPath(chain.ToArray(), context.Scope.GetUsingNamespaces());
					}
					return true;
				case eToken.GlobalIdentifier:
					usedTokens = 1;
					node = new SGlobalIdentifier(lead);
					return true;
				case eToken.True:
				case eToken.False:
					usedTokens = 1;
					node = new SLiteralBool(lead);
					return true;
				case eToken.Null:
					usedTokens = 1;
					node = new SLiteralNull();
					return true;
				case eToken.OneLiner:
					{
						int next = 1;
						for (; next < currLine.Length; next++)
							if (currLine[next].Token.Type == eToken.OneLiner) break;
						if (next >= currLine.Length) throw new ParserException("missing \"`\"", lead);

						usedTokens = next + 1;
						var subset = util.RangeSubset(currLine, 1, next - 1);
						if (!TryParseExpression(context, subset, out var exp, out _))
							return false;
						node = new SOneLiner(exp);
						return true;
					}
				case eToken.Lambda:
					{
						if (currLine.Length > 2 && currLine[1].Token.Type == eToken.LeftParen)
						{
							var split = LineParser.ParseParameterDefs(util.RangeRemainder(currLine, 1), out usedTokens);
							usedTokens += 2;
							if (currLine.Length > usedTokens + 2 && currLine[usedTokens].Token.Type == eToken.LeftBracket)
							{
								var caps = ParseCaptureVariables(util.RangeRemainder(currLine, usedTokens), out var usedCaps);
								usedTokens += usedCaps;
								node = new SLambdaMethod(split.Item1, split.Item2, caps, split.Item3, split.Item4, split.Item5);
								return true;
							}
							else
							{
								node = new SLambdaMethod(split.Item1, split.Item2, null, split.Item3, split.Item4, split.Item5);
								return true;
							}
						}
						else if (currLine.Length > 2 && currLine[1].Token.Type == eToken.LeftBracket)
						{
							var caps = ParseCaptureVariables(util.RangeRemainder(currLine, 1), out var usedCaps);
							usedTokens += usedCaps + 1;
							node = new SLambdaMethod(null, null, caps, false, false, false);
							return true;
						}
						else
						{
							usedTokens = 1;
							node = new SLambda();
							return true;
						}
					}
				case eToken.Switch:
					{
						if (currLine.Length > 2 && currLine[1].Token.Type == eToken.LeftParen)
						{
							var read = ScanAhead(currLine, 2, 0, false);
							if (!TryParseExpression(context, read, out var exp, out int use))
								throw new ParserException("expected expression", currLine[1]);
							node = new SSwitchExpression(exp);
							usedTokens = use + 3;
							return true;
						}
						else
						{
							usedTokens = 1;
							node = new SSwitchExpression();
							return true;
						}
					}
				case eToken.Dim:
					{
						if (currLine.Length > 2 && currLine[1].Token.Type == eToken.LeftParen)
						{
							var read = ScanAhead(currLine, 2, 0, false);
							node = new SDimInline(context, read);
							usedTokens = 3 + read.Length;
							if (usedTokens > currLine.Length)
								throw new ParserException("expected )", currLine[0]);
						}
						else
						{
							var read = ScanAhead(currLine, 1, BaseToken.OpPriority(eToken.Dot));
							if (read.Length > 0 && TryParseExpression(context, read, out var exp, out int use, halt))
							{
								node = new SDim(exp);
								usedTokens = use + 1;
							}
							else
							{
								usedTokens = 1;
								node = new SDim();
							}
						}
						return true;
					}
				case eToken.TextData:
					{
						usedTokens = 1;
						node = new STextData();
						return true;
					}
				case eToken.Free:
					{
						var isMaybe = (currLine.Length > 1 && currLine[1].Token.Type == eToken.QuestionMark);
						var read = ScanAhead(currLine, isMaybe ? 2 : 1, BaseToken.OpPriority(eToken.Dot));
						if (read.Length > 0 && TryParseExpression(context, read, out var exp, out int use))
						{
							node = new SFree(exp, isMaybe);
							usedTokens = use + 1 + (isMaybe ? 1 : 0);
						}
						else throw new ParserException("could not parse expression for free", lead);
						return true;
					}
				case eToken.Await:
					{
						var read = ScanAhead(currLine, 1, BaseToken.OpPriority(eToken.Dot));
						if (read.Length == 0 || !TryParseExpression(context, read, out var exp, out int use, halt))
							throw new ParserException("could not parse await", currLine[0]);
						if (exp is ICanAwait)
							(exp as ICanAwait).FlagAsAwaiting();
						else
							throw new ParserException("unknown expression for await", currLine[0]);
						node = new SAwait(exp);
						usedTokens = use + 1;
						return true;
					}
				case eToken.Arun:
					{
						var read = ScanAhead(currLine, 1, BaseToken.OpPriority(eToken.Dot), true, true);
						if (read.Length == 0 || !TryParseExpression(context, read, out var exp, out int use, halt))
							throw new ParserException("could not parse arun", currLine[0]);
						node = new SArun(exp);
						usedTokens = use + 1;
						return true;
					}
				case eToken.Try:
					{
						var left = ScanAhead(currLine, 1, BaseToken.OpPriority(eToken.Dot), true, true, true);
						if (!TryParseExpression(context, left, out var test, out var lUsed, eToken.Catch))
							throw new ParserException("could not parse try expression", currLine[0]);
						if (lUsed + 1 < currLine.Length && currLine[1 + lUsed].Token.Type == eToken.Catch)
						{
							var right = ScanAhead(currLine, lUsed + 2, BaseToken.OpPriority(eToken.Dot), true, true);
							if (!TryParseExpression(context, right, out var caught, out var rUsed))
								throw new ParserException("could not parse catch expression", currLine[0]);
							usedTokens = lUsed + rUsed + 2;
							node = new STryExpression(test, caught);
						}
						else
						{
							usedTokens = lUsed + 1;
							node = new STryExpression(test);
						}
						return true;
					}
				case eToken.Throw:
					{
						var read = ScanAhead(currLine, 1, BaseToken.OpPriority(eToken.Dot), true, true);
						if (read.Length == 0)
						{
							node = new SThrowExpression();
							usedTokens = 1;
						}
						else if (TryParseExpression(context, read, out var exp, out int use, halt))
						{
							node = new SThrowExpression(exp);
							usedTokens = use + 1;
						}
						else
							throw new ParserException("could not parse throw", currLine[0]);
						return true;
					}
				case eToken.Avow:
					{
						var read = ScanAhead(currLine, 1, BaseToken.OpPriority(eToken.Dot), true, true);
						if (!TryParseExpression(context, read, out var test, out var lUsed))
							throw new ParserException("could not parse try expression", currLine[0]);
						usedTokens = lUsed + 1;
						node = new SAvowExpression(test);
						return true;
					}
				default:
					break;
			}
			return false;
		}
		static SExpressionComponent CollapseExpression(Stack<SExpressionComponent> components, int prevPriorty = 0)
		{
			if (components.Count == 0)
				throw new ParserException("expected expression to continue");

			var next = components.Pop();
			if (next is SOperand)
			{
				var nop = next as SOperand;
				if (!BaseToken.IsUnary(nop.Type))
					throw new ParserException("not expecting operand");
				var readAhead = CollapseExpression(components, BaseToken.UnaryPriority(nop.Type));
				if (readAhead == null) throw new ParserException("expected expression after unary");
				next = new SUnary(nop, readAhead);
			}
			if (components.Count == 0) return next;
			var op = components.Peek() as SOperand;
			if (op == null) throw new ParserException("something is fishy...");
			var pri = BaseToken.OpPriority(op.Type);
			if (pri <= prevPriorty) // a*b+c, processing b(+)
				return next;

			components.Pop();
			SExpressionComponent nextExact = null;
			if (components.Count > 0) nextExact = components.Peek();
			
			var readAheadPri = BaseToken.OpPriority(op.Type);
			if (BaseToken.OpChainsRight(op.Type))
				readAheadPri--;
			var rightSide = CollapseExpression(components, readAheadPri);
			SExpressionComponent combine;

			// a+b*c+d, processing b(*)
			if (op.Type == eToken.Dot)
				combine = new SScopeAccess(next, rightSide);
			else if (op.Type == eToken.DotQuestion)
				combine = new SScopeMaybeAccess(next, rightSide, nextExact as IHaveIdentifierSymbol, false);
			else if (op.Type == eToken.QuestionDot)
				combine = new SScopeMaybeAccess(next, rightSide, nextExact as IHaveIdentifierSymbol, true);
			else if (op.Type == eToken.LeftBracket)
				combine = new SKeyAccess(next, rightSide as SParamList);
			else if (op.Type == eToken.Has)
				combine = new SHasProperty(next, rightSide);
			else
				combine = new SOperation(next, op, rightSide);

			if (components.Count == 0) return combine;

			components.Push(combine); // push (a+(b*c)) back on, and process that+d
			return CollapseExpression(components, prevPriorty);
		}

		// naive parser looking for possible variables to close, probably some odd cases
		internal static List<string> ScanForClosureVars(RelativeTokenReference[] tokes)
		{
			HashSet<string> idents = new HashSet<string>();
			for (int i = 0; i < tokes.Length; i++)
			{
				var curr = tokes[i].Token;
				//ignore anything following a . I can't think of a scenario where we want to capture that
				if (curr.Type == eToken.Dot) { i++; continue; }
				if (curr.Type == eToken.Identifier)
					if (!idents.Contains(curr.Token))
						idents.Add(curr.Token);
			}
			return idents.ToList();
		}

		internal static List<SExpressionComponent> ParseParameterList(ParseContext context, RelativeTokenReference[] currLine, params eToken[] halt)
		{
			// assumes list is wrapped in ()
			var inner = util.RangeSubset(currLine, 1, currLine.Length - 2);
			return ParseExpressionList(context, inner, halt);
		}

		internal static List<SExpressionComponent> ParseExpressionList(ParseContext context, RelativeTokenReference[] currLine, params eToken[] halt)
		{
			TryParseExpressionList(context, currLine, out var list, out _, halt);
			return list;
		}

		//returns false if list is empty. hard to say if that's the right move
		internal static bool TryParseExpressionList(ParseContext context, RelativeTokenReference[] currLine, out List<SExpressionComponent> list, out int UsedTokens, params eToken[] halt)
		{
			UsedTokens = 0;
			list = new List<SExpressionComponent>();
			if (currLine.Length == 0)
				return false;
			RelativeTokenReference[] subset;
			var endCopy = new eToken[halt.Length + 1];
			for (int i = 0; i < halt.Length; i++)
				endCopy[i] = halt[i];
			endCopy[endCopy.Length - 1] = eToken.Comma;

			while (true)
			{
				subset = util.RangeRemainder(currLine, UsedTokens);
				if (!TryParseExpression(context, subset, out var next, out var used, endCopy))
					return false;
				UsedTokens += used;
				list.Add(next);
				if (used == subset.Length) return (list.Count > 0);
				var peek = subset[used];
				if (peek.Token.Type != eToken.Comma) return (list.Count > 0);
				UsedTokens++;
			}
		}

		internal static RelativeTokenReference[] ScanAhead(RelativeTokenReference[] tokens, int begin, int haltOpPriority = 0, bool haltOnComma = true, bool mightBeOneLiner = false, bool haltOnCatch = false)
		{
			var i = FindEnd(tokens, begin, haltOpPriority, haltOnComma, mightBeOneLiner, haltOnCatch);
			return util.RangeSubset(tokens, begin, i - begin);
		}
		internal static int FindEnd(RelativeTokenReference[] tokens, int begin, int haltOpPriority = 0, bool haltOnComma = true, bool mightBeOneLiner = false, bool haltOnCatch = false)
		{
			int i;
			Stack<eToken> closing = new Stack<eToken>();
			Dictionary<eToken, eToken> pairedTokens = new Dictionary<eToken, eToken>()
				{
					{eToken.LeftParen,  eToken.RightParen },
					{eToken.LeftBracket,  eToken.RightBracket },
					{eToken.LeftBrace,  eToken.RightBrace },
					{eToken.OneLiner, eToken.OneLiner },
				};
			var halt = new HashSet<eToken>(pairedTokens.Values);
			if (haltOnComma) halt.Add(eToken.Comma);
			if (haltOnCatch) halt.Add(eToken.Catch);
			for (i = begin; i < tokens.Length; i++)
			{
				var t = tokens[i].Token;
				if (mightBeOneLiner && i == begin && t.Type == eToken.OneLiner) // arun `$foo()`
					closing.Push(t.Type);
				else if (halt != null && halt.Contains(t.Type) && closing.Count == 0)
					break;
				else if (t.Type == eToken.BackSlash)
					break;
				else if (t.Type == eToken.SemiColon)
					break;
				else if (pairedTokens.ContainsKey(t.Type))
					closing.Push(pairedTokens[t.Type]);
				else if (closing.Count > 0 && closing.Peek() == t.Type)
					closing.Pop();
				else if (closing.Count == 0 && BaseToken.IsOperand(t.Type) && BaseToken.OpPriority(t.Type) < haltOpPriority)
					break;
			}
			return i;
		}

		// expects leading [ and includes trailing ]
		internal static List<SLambdaCaptureVariable> ParseCaptureVariables(RelativeTokenReference[] currLine, out int usedTokens)
		{
			if (currLine.Length < 2 || currLine[0].Token.Type != eToken.LeftBracket)
				throw new ParserException("could not parse capture", currLine[0]);
			usedTokens = 1;
			for (int i = 1; i < currLine.Length; i++)
				if (currLine[i].Token.Type == eToken.RightBracket)
				{
					usedTokens = i + 1;
					break;
				}
			if (usedTokens < 2) throw new ParserException("could not parse capture", currLine[currLine.Length - 1]);

			var trimmed = util.RangeSubset(currLine, 1, usedTokens - 2);
			var split = util.SplitArr(trimmed, t => t.Token.Type == eToken.Comma);
			List<SLambdaCaptureVariable> caps = new List<SLambdaCaptureVariable>();
			foreach (var pc in split)
			{
				if (pc.Length == 1 && pc[0].Token.Type == eToken.Identifier)
					caps.Add(new SLambdaCaptureVariable(pc[0], SLambdaCaptureVariable.eType.Reference));
				else if (pc.Length == 2 && pc[1].Token.Type == eToken.Identifier && pc[0].Token.Type == eToken.Copy)
					caps.Add(new SLambdaCaptureVariable(pc[1], SLambdaCaptureVariable.eType.Copy));
				else if (pc.Length == 2 && pc[1].Token.Type == eToken.Identifier && pc[0].Token.Type == eToken.Free)
					caps.Add(new SLambdaCaptureVariable(pc[1], SLambdaCaptureVariable.eType.Free));
				else if (pc.Length == 3 && pc[2].Token.Type == eToken.Identifier && pc[0].Token.Type == eToken.Free && pc[1].Token.Type == eToken.QuestionMark)
					caps.Add(new SLambdaCaptureVariable(pc[2], SLambdaCaptureVariable.eType.Freeish));
			}
			return caps;
		}
	}
}
