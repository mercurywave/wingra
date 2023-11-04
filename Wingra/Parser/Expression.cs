using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	// stand alone expression, like a function call
	class SExpression : SStatement, IExportGlobalSymbol, ICanEmitInline
	{
		SExpressionComponent _exp;
		public SExpression(int fileLine, SExpressionComponent exp) : base(fileLine)
		{
			_exp = exp;
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_exp is SOperation && (_exp as SOperation).Op.Type == eToken.EqualSign)
				throw new CompilerException("'=' does not mean assignment, use ':' instead", func.CurrentFileLine);
			_exp.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			func.Add(asmStackLevel, eAsmCommand.ClearRegisters, 1); // ignore any potential return
		}
		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_exp.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			// caller will add the clear register
			return true;
		}

		public IEnumerable<string> GetExportableSymbolsInside(SyntaxNode parent)
		{
			if (_exp is IExportGlobalSymbol)
				return (_exp as IExportGlobalSymbol).GetExportableSymbolsInside(this);
			return new List<string>();
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _exp;
		}


		internal SExpressionComponent Component => _exp;
	}
	public class SExpressionComponent : SyntaxNode
	{
		public virtual IEnumerable<SExpressionComponent> IterExpChildren()
		{
			return new SExpressionComponent[0];
		}
		internal override void OnAddedToTree(ParseContext context)
		{
			var nodeList = IterExpChildrenRecurseive().ToList();
			foreach (var exp in nodeList)
				exp.OnAddedToTree(context);
			base.OnAddedToTree(context);
		}
		public IEnumerable<SExpressionComponent> IterExpChildrenRecurseive()
		{
			var list = IterExpChildren().ToList();
			foreach (var child in list.ToList())
				list.AddRange(child.IterExpChildrenRecurseive());
			return list;
		}

		internal virtual void EmitAsAssignment(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{ throw new ParserException("Expression can't be evaluated as a target for assignment"); }

		internal virtual void EmitAsFree(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent, bool allowPointerSteal)
		{ throw new ParserException("Expression can't be evaluated as a target for free"); }

		public bool? TryCompileTimeInlineBool(Compiler compiler, FileAssembler file, ErrorLogger errors)
		{
			var emitter = this as ICanEmitInline;
			if (emitter == null) return null;
			var func = new FunctionFactory();
			var err = new MinimalErrorLogger();
			if (!emitter.TryEmitInline(compiler, file, func, 0, err, this))
				return null;
			if (err.AnyLogged) return null;
			var ass = func.Assemble(compiler, file, errors);
			if (ass.Count != 2) return null;
			if (ass[0].Command != eAsmCommand.PushBool) return null;
			return (ass[0].Param != 0);
		}
	}
	class SLiteralString : SExpressionComponent, ICanEmitInline
	{
		RelativeTokenReference? _source;
		public string Content => _source.Value.Token.Token;
		public SLiteralString(RelativeTokenReference? toke)
		{
			_source = toke;
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (!_source.HasValue)
				func.Add(asmStackLevel, eAsmCommand.PushString, "");
			else
			{
				var encoded = EncodeString(_source.Value.Token.Token);
				func.Add(asmStackLevel, eAsmCommand.PushString, 0, encoded);
			}
		}

		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
			return true;
		}
		public static string EncodeString(string source)
			=> source.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"");
	}

	class SInterpString : SExpressionComponent
	{
		RelativeTokenReference? _source => _strings[0];
		List<RelativeTokenReference?> _strings;
		List<SExpressionComponent> _insertExpressions; // should be one less of these then there are _strings
		public SInterpString(List<RelativeTokenReference?> strings, List<SExpressionComponent> insertExpressions)
		{
			_strings = strings;
			_insertExpressions = insertExpressions;
		}

		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			return _insertExpressions;
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			string Encode(RelativeTokenReference? toke) => toke == null ? "" : SLiteralString.EncodeString(toke.Value.Token.Token);
			if (_strings.Count == 0)
				func.Add(asmStackLevel, eAsmCommand.PushString, "");
			else if (_strings.Count == 1)
				func.Add(asmStackLevel, eAsmCommand.PushString, 0, Encode(_strings[0]));
			else
			{
				int added = 0;
				for (int i = 0; i < _strings.Count; i++)
				{
					if (_strings[i].HasValue)
					{
						func.Add(asmStackLevel, eAsmCommand.PushString, 0, Encode(_strings[i]));
						added++;
					}
					if (i < _insertExpressions.Count)
					{
						_insertExpressions[i].EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
						// there's an argument for not inserting an auto-meh to the output, but it's really gross to add it to every number insert
						// would probably make sense to make a dedicated op for compiling all the strings together which can handle this
						func.Add(asmStackLevel, eAsmCommand.Meh);
						added++;
					}
				}
				for (int i = 0; i < added - 1; i++)
					func.Add(asmStackLevel, eAsmCommand.Add);
			}
		}
	}

	class SLiteralNumber : SExpressionComponent, ICanEmitInline
	{
		// this is slightly dumb because "-" is a seperate symbol to the number
		RelativeTokenReference _source;
		string _content;
		public SLiteralNumber(RelativeTokenReference[] tokes)
		{
			_source = tokes[tokes.Length - 1];
			_content = util.Join(tokes.Select(t => t.Token.Token), "");
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_content.Contains("."))
				func.Add(asmStackLevel, eAsmCommand.PushFloat, float.Parse(_content, System.Globalization.CultureInfo.InvariantCulture));
			else
				func.Add(asmStackLevel, eAsmCommand.PushInt, int.Parse(_source.Token.Token, System.Globalization.CultureInfo.InvariantCulture));
		}
		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
			return true;
		}
		public int iValue => int.Parse(_content);
		public float fValue => float.Parse(_content, System.Globalization.CultureInfo.InvariantCulture);
		public bool IsFloat => _content.Contains(".");
	}

	class SLiteralBool : SExpressionComponent, ICanEmitInline
	{
		bool _value;
		public SLiteralBool(RelativeTokenReference toke)
		{
			_value = toke.Token.Type == eToken.True;
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.PushBool, Value ? 1 : 0);
		}
		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
			return true;
		}
		public bool Value => _value;
	}

	class SLiteralNull : SExpressionComponent, ICanEmitInline
	{
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.PushNull);
		}
		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
			return true;
		}
	}

	class SCompileConst : SExpressionComponent, ICanEmitInline
	{
		Variable _val;
		public SCompileConst(Variable value) { _val = value; }
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_val.IsInt)
				func.Add(asmStackLevel, eAsmCommand.PushInt, _val.AsInt());
			else if (_val.IsBool)
				func.Add(asmStackLevel, eAsmCommand.PushBool, _val.AsBool() ? 1 : 0);
			else if (_val.IsFloat)
				func.Add(asmStackLevel, eAsmCommand.PushFloat, _val.AsFloat());
			else if (_val.IsString)
				func.Add(asmStackLevel, eAsmCommand.PushString, _val.AsString());
			else throw new NotImplementedException();
		}
		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
			return true;
		}
	}

	class SIdentifier : SExpressionComponent, ICanBeProperty, IHaveLocalIdentifierSymbol
	{
		internal RelativeTokenReference _source;
		public SIdentifier(RelativeTokenReference toke)
		{
			_source = toke;
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.AssertVarDefined(_source);
			func.Add(asmStackLevel, eAsmCommand.Load, 0, _source.Token.Token);
		}

		public void EmitPropertyAction(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.DotAccess, 0, Symbol);
		}
		public string Symbol => _source.Token.Token;

		internal override void EmitAsAssignment(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.StoreLocal, Symbol);
		}

		internal override void EmitAsFree(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent, bool allowPointerSteal)
		{
			if (allowPointerSteal)
				func.Add(asmStackLevel, eAsmCommand.SoftFreeLocal, Symbol);
			else
				func.Add(asmStackLevel, eAsmCommand.FreeLocal, Symbol);
		}
	}
	class SGlobalIdentifier : SExpressionComponent, IHaveIdentifierSymbol
	{
		internal RelativeTokenReference _source;
		string _fileKey;
		public SGlobalIdentifier(RelativeTokenReference toke)
		{
			_source = toke;
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			_fileKey = context.FileKey;
			base.OnAddedToTree(context);
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitContext(compiler, func, asmStackLevel);
			func.Add(asmStackLevel, eAsmCommand.LoadScratch, 0, Symbol);
		}
		public string Symbol => _source.Token.Token.Replace("^", "");

		internal void EmitContext(Compiler compiler, FunctionFactory func, int asmStackLevel)
		{
			if (!compiler.StaticMap.TryResolveScratch(Symbol, _fileKey, out var targetFile))
				throw new CompilerException("scratch not defined ^" + Symbol, compiler.FileLine, _source);
			func.Add(asmStackLevel, eAsmCommand.SetFileContext, targetFile);
		}

		internal override void EmitAsAssignment(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitContext(compiler, func, asmStackLevel);
			func.Add(asmStackLevel, eAsmCommand.StoreNewScratch, Symbol);
		}

		internal override void EmitAsFree(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent, bool allowPointerSteal)
		{
			if (allowPointerSteal) throw new NotImplementedException();
			EmitContext(compiler, func, asmStackLevel);
			func.Add(asmStackLevel, eAsmCommand.FreeScratch, Symbol);
		}
	}

	class SUnary : SExpressionComponent, ICanEmitInline
	{
		SOperand _type;
		SExpressionComponent _exp;
		public SUnary(SOperand type, SExpressionComponent exp)
		{
			_type = type;
			_exp = exp;
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var type = _type.Type;
			if (_exp is SLiteralNumber && type == eToken.Subtract)
			{
				var num = _exp as SLiteralNumber;

				if (num.IsFloat)
					func.Add(asmStackLevel, eAsmCommand.PushFloat, -(_exp as SLiteralNumber).fValue);
				else
					func.Add(asmStackLevel, eAsmCommand.PushInt, -(_exp as SLiteralNumber).iValue);
				return;
			}
			// negation is a bit of a hack - we'll push 0 and subtract from that
			else if (type == eToken.Subtract)
				func.Add(asmStackLevel, eAsmCommand.PushInt, 0);

			_exp.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);

			eAsmCommand cmd;
			if (type == eToken.Not)
				cmd = eAsmCommand.Not;
			else if (type == eToken.Subtract)
				cmd = eAsmCommand.Subtract;
			else if (type == eToken.Copy)
				cmd = eAsmCommand.Copy;
			else if (type == eToken.Meh)
				cmd = eAsmCommand.Meh;
			else throw new NotImplementedException();
			func.Add(asmStackLevel, cmd);
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _type;
			yield return _exp;
		}

		public bool TryEmitInline(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var type = _type.Type;
			if (type == eToken.Not || type == eToken.Subtract)
			{
				var emitter = _exp as ICanEmitInline;
				if (emitter == null) return false;
				var func2 = new FunctionFactory();
				var err = new MinimalErrorLogger();
				if (!emitter.TryEmitInline(compiler, file, func2, 0, err, this))
					return false;
				if (err.AnyLogged) return false;
				var ass = func2.Assemble(compiler, file, errors);
				if (ass.Count != 2) return false;

				if (type == eToken.Not)
				{
					if (ass[0].Command != eAsmCommand.PushBool) return false;
					func.Add(asmStackLevel, eAsmCommand.PushBool, ass[0].Param != 0 ? 0 : 1);
					return true;
				}

				if (type == eToken.Subtract)
				{
					if (ass[0].Command != eAsmCommand.PushInt) return false;
					func.Add(asmStackLevel, eAsmCommand.PushInt, -ass[0].Param);
					return true;
				}
			}
			return false;
		}
	}

	class SOperand : SExpressionComponent
	{
		public SOperand(eToken toke)
		{
			Type = toke;
		}
		public eToken Type;
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			eAsmCommand cmd;
			if (Type == eToken.Add)
				cmd = eAsmCommand.Add;
			else if (Type == eToken.Subtract)
				cmd = eAsmCommand.Subtract;
			else if (Type == eToken.Multiply)
				cmd = eAsmCommand.Multiply;
			else if (Type == eToken.Divide)
				cmd = eAsmCommand.Divide;
			else if (Type == eToken.EqualSign)
				cmd = eAsmCommand.Equals;
			else if (Type == eToken.NotEquals)
				cmd = eAsmCommand.NotEquals;
			else if (Type == eToken.Greater)
				cmd = eAsmCommand.GreaterThan;
			else if (Type == eToken.EqGreater)
				cmd = eAsmCommand.EqGreater;
			else if (Type == eToken.Less)
				cmd = eAsmCommand.LessThan;
			else if (Type == eToken.EqLess)
				cmd = eAsmCommand.EqLess;
			else if (Type == eToken.And)
				cmd = eAsmCommand.And; // TODO: these aren't actually needed due to short circuit
			else if (Type == eToken.Or)
				cmd = eAsmCommand.Or;
			else if (Type == eToken.QuestionMark)
				cmd = eAsmCommand.NullCoalesce;
			else throw new NotImplementedException();
			func.Add(asmStackLevel, cmd);
		}
	}

	class SOperation : SExpressionComponent
	{
		SExpressionComponent _a;
		SOperand _op;
		SExpressionComponent _b;
		public SOperation(SExpressionComponent a, SOperand op, SExpressionComponent b)
		{
			_a = a;
			_op = op;
			_b = b;
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_a.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			if (BaseToken.DoesShortCircuit(_op.Type))
			{
				if (_op.Type == eToken.And)
					func.Add(asmStackLevel, eAsmCommand.ShortCircuitFalse);
				else if (_op.Type == eToken.Or)
					func.Add(asmStackLevel, eAsmCommand.ShortCircuitTrue);
				else if (_op.Type == eToken.QuestionMark)
					func.Add(asmStackLevel, eAsmCommand.ShortCircuitNotNull);
				else throw new NotImplementedException();
				func.Add(asmStackLevel + 1, eAsmCommand.Pop);
				_b.EmitAssembly(compiler, file, func, asmStackLevel + 1, errors, this);
			}
			else
			{
				_b.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				_op.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			}
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _a;
			yield return _op;
			yield return _b;
		}
		internal SOperand Op => _op;
	}

	class SParamList : SExpressionComponent
	{
		List<SExpressionComponent> _list;
		public int Count => _list.Count;
		public SParamList(List<SExpressionComponent> list)
		{
			_list = list;
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			foreach (var p in _list)
				p.EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			foreach (var p in _list)
				yield return p;
		}
	}

	class SHasProperty : SExpressionComponent
	{
		SExpressionComponent _obj;
		string _prop;
		public SHasProperty(SExpressionComponent obj, SExpressionComponent prop)
		{
			if (prop == null) throw new ParserException("could not parse named property");
			_obj = obj;
			if (prop is SIdentifier)
				_prop = (prop as SIdentifier).Symbol;
			else if (prop is SLiteralString)
				_prop = (prop as SLiteralString).Content;
			else throw new ParserException("'has' expects identifier or string on right side");
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_obj.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			func.Add(asmStackLevel, eAsmCommand.Has, _prop);
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _obj;
		}
	}
	class SOneLiner : SExpressionComponent
	{
		SExpressionComponent _exp;
		public SOneLiner(SExpressionComponent exp)
		{
			_exp = exp;
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var sub = file.GenLambda();
			sub.Add(0, eAsmCommand.IgnoreParams);
			_exp.EmitAssembly(compiler, file, sub, 0, errors, this);
			sub.Add(0, eAsmCommand.Return, 1);
			func.Add(asmStackLevel, eAsmCommand.CreateLambda, 0, sub.UniqNameInFile);
		}

		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _exp;
		}
	}

	class SInlineSub : SExpressionComponent, IHaveChildScope
	{
		SyntaxScopeHelper _scope = new SyntaxScopeHelper();
		public IEnumerable<SyntaxNode> Children => _scope.Children;
		List<string> _identifiers;
		public SInlineSub(List<string> identifiers)
		{
			_identifiers = identifiers;
		}
		public virtual bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return LineParser.TryParseStatement(context, currLine, out node, out usedTokens);
		}
		public void AddBlankLine() { }
		public void AddChild(SyntaxNode node)
		{
			_scope.Add(node);
		}
		protected void EmitChildren(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors)
		{
			_scope.EmitChildren(compiler, this, file, func, asmStackLevel, errors);
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var sub = file.GenLambda();
			sub.Add(0, eAsmCommand.IgnoreParams);
			EmitChildren(compiler, file, sub, 0, errors);
			func.Add(asmStackLevel, eAsmCommand.CreateLambda, 0, sub.UniqNameInFile);
		}
	}

	class SLambda : SExpressionComponent, IHaveChildScope
	{
		SyntaxScopeHelper _scope = new SyntaxScopeHelper();

		public void AddChild(SyntaxNode node)
			=> _scope.Add(node);

		public bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return LineParser.TryParseStatement(context, currLine, out node, out usedTokens);
		}
		public void AddBlankLine() { }

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var sub = file.GenLambda(func.CurrentFileLine, true);
			sub.Add(0, eAsmCommand.IgnoreParams);
			_scope.EmitChildren(compiler, this, file, sub, 0, errors);

			func.Add(asmStackLevel, eAsmCommand.CreateLambda, sub.UniqNameInFile);
		}
	}

	// lambda with defined parameters
	class SLambdaMethod : SExpressionComponent, IHaveChildScope
	{
		SyntaxScopeHelper _scope = new SyntaxScopeHelper();
		List<SLambdaCaptureVariable> _captures;
		List<SParameter> _parameters;
		List<SIdentifier> _returnParams;
		bool _doesYield;
		bool _isAsync;
		bool _doesThrow;
		public SLambdaMethod(List<SParameter> parameters, List<SIdentifier> returnParams, List<SLambdaCaptureVariable> captures, bool doesYield, bool isAsync, bool doesThrow)
		{
			_parameters = parameters;
			_returnParams = returnParams;
			_captures = captures;
			_doesYield = doesYield;
			_isAsync = isAsync;
			_doesThrow = doesThrow;
		}

		public void AddChild(SyntaxNode node)
			=> _scope.Add(node);

		public bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return LineParser.TryParseStatement(context, currLine, out node, out usedTokens);
		}
		public void AddBlankLine() { }

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var lamb = file.GenLambda(func.CurrentFileLine, _parameters == null);
			lamb.IsAsync = _isAsync;
			lamb.CanThrow = _doesThrow;
			lamb.AllowInjection = true;
			if (_captures != null)
				foreach (var cap in _captures)
					lamb.ManuallyAssumeVariable(cap.Identifier);
			if (_parameters != null)
			{
				foreach (var p in _parameters)
				{
					lamb.RegisterInjectableParameter(p.Identifier);
					p.EmitAssembly(compiler, file, lamb, 0, errors, this);
				}
			}
			if (_returnParams != null)
			{
				lamb.AddReturnParam(_returnParams.Select(r => r.Symbol).ToArray());
				if (_doesYield) lamb.SetupIterator(_returnParams.Count);
			}
			_scope.EmitChildren(compiler, this, file, lamb, 0, errors);

			if (_captures == null)
				func.Add(asmStackLevel, eAsmCommand.CreateLambda, lamb.UniqNameInFile);
			else
			{
				func.Add(asmStackLevel, eAsmCommand.CreateManualLambda, lamb.UniqNameInFile);
				foreach (var cap in _captures)
					cap.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			}
		}
	}

	class SLambdaCaptureVariable : SyntaxNode
	{
		RelativeTokenReference _token;
		public string Identifier => _token.Token.Token;
		public enum eType { Reference, Copy, Free, Freeish }
		eType _type;
		public SLambdaCaptureVariable(RelativeTokenReference identifier, eType type) : base()
		{
			_token = identifier;
			_type = type;
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.ManuallyAssumeVariable(_token.Token.Token);
			func.Add(asmStackLevel, GetCommand(), _token.Token.Token);
		}
		eAsmCommand GetCommand()
		{
			switch (_type)
			{
				case eType.Reference: return eAsmCommand.CaptureVar;
				case eType.Copy: return eAsmCommand.CaptureCopy;
				case eType.Free: return eAsmCommand.CaptureFree;
				case eType.Freeish: return eAsmCommand.CaptureFreeish;
				default: throw new NotImplementedException();
			}
		}
	}

	class SExecute : SExpressionComponent, ICanAwait, IDecompose
	{
		internal List<SExpressionComponent> _params; // may be null!
		bool _isAsync;
		int _numToDecompose = 1;
		public SExecute(List<SExpressionComponent> param)
		{
			_params = param;
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			// we may be able to support one-liners
			if (_params.Count >= 1 && _params.Skip(1).All(s => s is SIdentifier))
			{
				foreach (SIdentifier p in _params.Skip(1))
				{
					func.AssertVarDefined(p._source);
					func.Add(asmStackLevel, eAsmCommand.PushString, 0, p.Symbol);
				}
				_params[0].EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				if (_isAsync) func.Add(asmStackLevel, eAsmCommand.BeginAwaitCall);
				func.Add(asmStackLevel, eAsmCommand.ExecNamed, _params.Count - 1);
			}
			else throw new NotImplementedException("I haven't implemented this case in SExecute");
			//TODO: else case!
			// wait, what is the else case? Is there one?

			func.Add(asmStackLevel, eAsmCommand.ReadReturn, _numToDecompose);
		}

		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			foreach (var p in _params)
				yield return p;
		}

		public void FlagAsAwaiting()
		{
			_isAsync = true;
		}
		public void RequestDecompose(int numRequested)
			=> _numToDecompose = numRequested;
	}

	// for @var : ...
	// emits as a load, which is maybe funky
	// this might be a very specialized thing
	// I can't think of any other good use case for this beyond for loops
	class SReserveIdentifierExp : SExpressionComponent, IHaveLocalIdentifierSymbol, IDeclareVariablesAtScope
	{
		internal RelativeTokenReference _source;
		public SReserveIdentifierExp(RelativeTokenReference toke)
		{
			_source = toke;
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.Load, Symbol);
		}

		public IEnumerable<string> GetDeclaredSymbolsInside(SyntaxNode parent)
		{
			yield return Symbol;
		}

		public string Symbol => _source.Token.Token;
		internal override void EmitAsAssignment(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.StoreNewLocal, Symbol);
		}
	}

	class SIgnoredVariable : SExpressionComponent, IHaveLocalIdentifierSymbol, IDeclareVariablesAtScope
	{
		RelativeTokenReference _token;
		public SIgnoredVariable(RelativeTokenReference token) : base() { _token = token; }
		public string Symbol => "%";

		public IEnumerable<string> GetDeclaredSymbolsInside(SyntaxNode parent)
		{
			yield return Symbol;
		}
		internal override void EmitAsAssignment(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.ReplaceOrNewLocal, Symbol);
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			throw new CompilerException("expected identifier", func.CurrentFileLine, _token);
		}
	}

	class SFunctionCall : SExpressionComponent, ICanBeProperty, IHaveLocalIdentifierSymbol, ICanAwait, IDecompose
	{
		internal SIdentifier _name;
		internal List<SExpressionComponent> _params; // may be null!
		bool _isAsync;
		int _numToDecompose = 1;
		public SFunctionCall(SIdentifier name, List<SExpressionComponent> param)
		{
			_name = name;
			_params = param;
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitParams(compiler, _params, file, func, asmStackLevel, errors, parent);
			var pct = _params != null ? _params.Count : 0;

			if (_isAsync) func.Add(asmStackLevel, eAsmCommand.BeginAwaitCall);
			if (pct > 0) func.Add(asmStackLevel, eAsmCommand.PassParams, pct);
			func.Add(asmStackLevel, eAsmCommand.CallFunc, 0, _name._source.Token.Token);

			func.Add(asmStackLevel, eAsmCommand.ReadReturn, _numToDecompose);
		}
		public static void EmitParams(Compiler compiler, List<SExpressionComponent> pars, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (pars != null)
				foreach (var p in pars)
					p.EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
		}

		public void EmitPropertyAction(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitParams(compiler, _params, file, func, asmStackLevel, errors, this);
			var pct = _params != null ? _params.Count : 0;
			func.Add(asmStackLevel, eAsmCommand.PassParams, pct);
			func.Add(asmStackLevel, eAsmCommand.CallMethod, 0, _name._source.Token.Token);

			func.Add(asmStackLevel, eAsmCommand.ReadReturn, _numToDecompose);
		}
		public string Symbol => _name.Symbol;

		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _name;
			if (_params != null)
				foreach (var p in _params)
					yield return p;
		}

		public void FlagAsAwaiting()
			=> _isAsync = true;
		public void RequestDecompose(int numRequested)
			=> _numToDecompose = numRequested;
	}

	interface IHaveLocalIdentifierSymbol : IHaveIdentifierSymbol
	{
	}
	interface IHaveIdentifierSymbol
	{
		string Symbol { get; }
	}

	interface IDecompose
	{
		void RequestDecompose(int numRequested);
	}

	interface ICanAwait
	{
		void FlagAsAwaiting();
	}
}
