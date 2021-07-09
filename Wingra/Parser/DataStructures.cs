using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	class SDim : SExpressionComponent, IHaveChildScope
	{
		SExpressionComponent _generator;
		public SDim(SExpressionComponent generator = null)
		{
			_generator = generator;
		}
		List<SDimElement> _children = new List<SDimElement>();
		List<SMixin> _mixins = new List<SMixin>();
		List<SDimMethod> _methods = new List<SDimMethod>();
		public bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			return (LineParser.TryParseDimChild(context, currLine, out node, out usedTokens));
		}
		public void AddChild(SyntaxNode node)
		{
			var dim = node as SDimElement;
			var mix = node as SMixin;
			var func = node as SDimMethod;
			if (dim == null && mix == null && func == null)
				throw new ParserException("expected mixin, value, function, or key:value");

			if (node != null && _children.Count > 0)
			{
				if (_children[0] is SDimAutoArrayNode && !(node is SDimAutoArrayNode))
					throw new ParserException("expected array style style dim init to continue");

				if (!(_children[0] is SDimAutoArrayNode) && node is SDimAutoArrayNode)
					throw new ParserException("expected key:value style dim init to continue");
			}
			if (mix != null)
				_mixins.Add(mix);
			else if (func != null)
				_methods.Add(func);
			else
				_children.Add(node as SDimElement);
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_children.Count == 0 && _generator == null && _mixins.Count == 0 && _methods.Count == 0)
			{
				func.Add(asmStackLevel, eAsmCommand.DimArray, 0);
				return;
			}

			var isList = (_children.Count == 0 || _children[0] is SDimAutoArrayNode) && (_mixins.Count == 0 && _methods.Count == 0);
			if (isList && (_mixins.Count > 0 || _methods.Count > 0))
				throw new CompilerException("cannot use list declaration with mixins", func.CurrentFileLine);
			if (_generator != null)
			{
				if (isList && _children.Count > 0)
					throw new CompilerException("cannot use list declaration with dim object initialization", func.CurrentFileLine);
				_generator.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			}
			else
			{
				var cmd = isList ? eAsmCommand.DimArray : eAsmCommand.DimDictionary;
				func.Add(asmStackLevel, cmd, _children.Count);
			}

			// technically these aren't in the order you might expect,
			// but you probably shouldn't write a mixin that reads the structure
			foreach (var mix in _mixins)
				mix.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);

			foreach (var metho in _methods)
				metho.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);

			if (isList)
				for (int i = 0; i < _children.Count; i++)
				{
					var arr = (_children[i] as SDimAutoArrayNode);
					if (arr != null) arr.Idx = i;
				}

			foreach (var c in _children)
				c.EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			if (_generator != null)
				yield return _generator;
			foreach (var node in _children)
				foreach (var exp in node.IterExpressionsRecursive())
					yield return exp;
			foreach (var node in _mixins)
				foreach (var exp in node.IterExpressionsRecursive())
					yield return exp;
		}
	}

	class SDimInline : SExpressionComponent
	{
		List<Tuple<SExpressionComponent, SExpressionComponent>> _data = new List<Tuple<SExpressionComponent, SExpressionComponent>>();
		public SDimInline(ParseContext context, RelativeTokenReference[] content)
		{
			if (content.Length == 0) return;
			while (content.Length > 0)
			{
				SExpressionComponent key, value;
				if (!ExpressionParser.TryParseExpression(context, content, out key, out var used, eToken.Colon, eToken.Comma))
					throw new ParserException("expected expression for dim()", content[0]);
				if (used >= content.Length)
				{
					_data.Add(new Tuple<SExpressionComponent, SExpressionComponent>(null, key));
					break;
				}
				var splitter = content[used];
				if (splitter.Token.Type == eToken.Colon)
				{
					var conRead = content.RangeRemainder(used + 1);
					if (!ExpressionParser.TryParseExpression(context, conRead, out value, out var valUsed, eToken.Colon, eToken.Comma))
						throw new ParserException("expected expression for dim() value", content[0]);
					used += valUsed + 1;
				}
				else { value = key; key = null; }
				_data.Add(new Tuple<SExpressionComponent, SExpressionComponent>(key, value));
				if (used + 1 >= content.Length) break;
				content = content.RangeRemainder(used + 1);
			}
			var keyCount = _data.Count(t => t.Item1 != null);
			if (keyCount > 0 && keyCount < _data.Count)
				throw new ParserException("cannot mix key/value and iterator style", content[0]);
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (_data.Count == 0)
			{
				func.Add(asmStackLevel, eAsmCommand.DimArray, 0);
				return;
			}
			var keyCount = _data.Count(t => t.Item1 != null);
			var isList = keyCount == 0;
			var cmd = isList ? eAsmCommand.DimArray : eAsmCommand.DimDictionary;
			func.Add(asmStackLevel, cmd, _data.Count);

			for (int i = 0; i < _data.Count; i++)
			{
				var pair = _data[i];
				pair.Item2.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
				if (pair.Item1 == null)
					func.Add(asmStackLevel, eAsmCommand.DimSetInt, i);
				else if (pair.Item1 is SIdentifier)
					func.Add(asmStackLevel, eAsmCommand.DimSetString, (pair.Item1 as SIdentifier).Symbol);
				else
				{
					pair.Item1.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
					func.Add(asmStackLevel, eAsmCommand.DimSetExpr);
				}
			}
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			foreach (var pair in _data)
			{
				if (pair.Item1 != null) yield return pair.Item1;
				yield return pair.Item2;
			}
		}
	}

	class SDimElement : SStatement
	{
		protected SExpressionComponent _value;
		public SDimElement(int fileLine, SExpressionComponent value) : base(fileLine)
		{
			_value = value;
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_value.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _value;
		}
	}
	class SDimAutoArrayNode : SDimElement
	{
		public int Idx;
		public SDimAutoArrayNode(int fileLine, SExpressionComponent right) : base(fileLine, right)
		{
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_value.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			func.Add(asmStackLevel, eAsmCommand.DimSetInt, Idx);
		}
	}

	class SDimLiteralKeyIdent : SDimElement
	{
		SIdentifier _left;
		public SDimLiteralKeyIdent(int fileLine, SIdentifier left, SExpressionComponent right) : base(fileLine, right)
		{
			_left = left;
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_value.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			func.Add(asmStackLevel, eAsmCommand.DimSetString, _left.Symbol);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _left;
			yield return _value;
		}
	}
	class SKeyValuePair : SDimElement
	{
		SExpressionComponent _left;
		public SKeyValuePair(int fileLine, SExpressionComponent left, SExpressionComponent right) : base(fileLine, right)
		{
			_left = left;
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_value.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			_left.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			func.Add(asmStackLevel, eAsmCommand.DimSetExpr);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _left;
			yield return _value;
		}
	}

	class SMixin : SStatement
	{
		RelativeTokenReference[] _path;
		List<string> _usingPaths;
		internal List<SExpressionComponent> _params; // may be null!
		string _fileKey;
		public SMixin(int fileLine, RelativeTokenReference[] path, List<string> usingPaths, List<SExpressionComponent> paramList) : base(fileLine)
		{
			if (path.Length < 1) throw new Exception("error parsing tokens for static path");
			_path = path;
			_usingPaths = usingPaths;
			_params = paramList;
		}
		internal override void OnAddedToTree(ParseContext context)
		{
			_fileKey = context.FileKey;
			base.OnAddedToTree(context);
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.SetupMixin);
			EmitParams(compiler, _params, file, func, asmStackLevel, errors, parent);

			var path = compiler.StaticMap.ResolvePath(_fileKey, func.CurrentFileLine, _path, _usingPaths, out var type, out _, out _, out var dynamicPath);
			if (dynamicPath.Length > 0)
				// implementing this is possible, but probably requires a chunk of code changes
				// I don't see a strong use case currently
				throw new NotImplementedException();
			func.Add(asmStackLevel, SStaticFunctionCall.GetCommandFromPath(type, true), path);
		}
		public static void EmitParams(Compiler compiler, List<SExpressionComponent> pars, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (pars == null || pars.Count == 0) return;
			foreach (var p in pars)
				p.EmitAssembly(compiler, file, func, asmStackLevel, errors, parent);
			func.Add(asmStackLevel, eAsmCommand.PassParams, pars.Count);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			return _params;
		}
	}


	class SScopeAccess : SExpressionComponent, ICanAwait, IWillDecompose
	{
		SExpressionComponent _obj;
		SExpressionComponent _prop;
		int _decompose = 1;
		public int NumToDecompose => _decompose;
		public SScopeAccess(SExpressionComponent obj, SExpressionComponent prop)
		{
			if (prop == null) throw new ParserException("could not parse named property");
			_obj = obj;
			_prop = prop;
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			if (parent is IWillDecompose) _decompose = (parent as IWillDecompose).NumToDecompose;
			_obj.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			if (_prop is ICanBeProperty)
				(_prop as ICanBeProperty).EmitPropertyAction(compiler, file, func, asmStackLevel, errors, this);
			else throw new CompilerException("unexpected type for scope access", func.CurrentFileLine);
		}
		internal override void EmitAsAssignment(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_obj.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			if (_prop is IHaveIdentifierSymbol)
				func.Add(asmStackLevel, eAsmCommand.StoreProperty, (_prop as IHaveIdentifierSymbol).Symbol);
			else throw new CompilerException("unexpected type for scope access", func.CurrentFileLine);
		}
		internal override void EmitAsFree(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent, bool allowPointerSteal)
		{
			if (allowPointerSteal) throw new NotImplementedException();
			_obj.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			if (_prop is IHaveIdentifierSymbol)
				func.Add(asmStackLevel, eAsmCommand.FreeProperty, (_prop as IHaveIdentifierSymbol).Symbol);
			else throw new ParserException("unexpected type for free");
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _obj;
			if (_prop != null) yield return _prop;
		}

		public void FlagAsAwaiting()
		{
			if (!(_prop is ICanAwait)) throw new ParserException("cannot await scoped property");
			(_prop as ICanAwait).FlagAsAwaiting();
		}
	}
	class SScopeMaybeAccess : SExpressionComponent, ICanAwait
	{
		SExpressionComponent _obj;
		SExpressionComponent _prop;
		bool _optOnLeft;
		public SScopeMaybeAccess(SExpressionComponent obj, SExpressionComponent prop, bool optOnLeft)
		{
			if (prop == null) throw new ParserException("could not parse named property");
			if (!optOnLeft && !(prop is IHaveIdentifierSymbol))
				throw new ParserException("expected named property");
			_obj = obj;
			_prop = prop;
			_optOnLeft = optOnLeft;
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_obj.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			if (_optOnLeft)
				func.Add(asmStackLevel, eAsmCommand.ShortCircuitNull);
			else
				func.Add(asmStackLevel, eAsmCommand.ShortCircuitPropNull, (_prop as IHaveIdentifierSymbol).Symbol);

			if (_prop is ICanBeProperty)
				(_prop as ICanBeProperty).EmitPropertyAction(compiler, file, func, asmStackLevel + 1, errors, this);
			else throw new ParserException("unexpected type for scope access");
		}
		internal override void EmitAsFree(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent, bool allowPointerSteal)
		{
			if (allowPointerSteal) throw new NotImplementedException();
			_obj.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			if (_optOnLeft)
				func.Add(asmStackLevel, eAsmCommand.ShortCircuitNull);
			else
			{
				var ident = (_prop as IHaveIdentifierSymbol)?.Symbol ?? "";
				func.Add(asmStackLevel, eAsmCommand.ShortCircuitPropNull, ident);
			}

			if (_prop is IHaveIdentifierSymbol)
				func.Add(asmStackLevel + 1, eAsmCommand.FreeProperty, (_prop as IHaveIdentifierSymbol).Symbol);
			else throw new ParserException("unexpected type for free");
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _obj;
			if (_prop != null) yield return _prop;
		}
		public void FlagAsAwaiting()
		{
			if (!(_prop is ICanAwait)) throw new ParserException("cannot await scoped property");
			(_prop as ICanAwait).FlagAsAwaiting();
		}
	}

	class SKeyAccess : SExpressionComponent
	{
		internal SExpressionComponent _leftSide;
		internal SParamList _params;
		public SKeyAccess(SExpressionComponent leftSide, SParamList keys)
		{
			_leftSide = leftSide;
			_params = keys;
			if (keys == null) throw new ParserException("key access can't find keys");
		}
		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitPrep(compiler, file, func, asmStackLevel, errors, parent);
			func.Add(asmStackLevel, eAsmCommand.KeyAccess, _params.Count);
		}
		public void EmitPrep(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_leftSide.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			_params.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
		}
		internal override void EmitAsAssignment(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitPrep(compiler, file, func, asmStackLevel, errors, parent);
			func.Add(asmStackLevel, eAsmCommand.KeyAssign, _params.Count);
		}
		internal override void EmitAsFree(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent, bool allowPointerSteal)
		{
			EmitPrep(compiler, file, func, asmStackLevel, errors, parent);
			func.Add(asmStackLevel, allowPointerSteal ? eAsmCommand.SoftFreeKey : eAsmCommand.KeyFree, _params.Count);
		}
		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _leftSide;
			yield return _params;
		}
	}

	class SFree : SExpressionComponent
	{
		SExpressionComponent _exp;
		bool _allowPointerSteal;
		public SFree(SExpressionComponent exp, bool allowPointerSteal = false) : base()
		{
			_exp = exp;
			_allowPointerSteal = allowPointerSteal;
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			_exp.EmitAsFree(compiler, file, func, asmStackLevel, errors, this, _allowPointerSteal);
		}

		public override IEnumerable<SExpressionComponent> IterExpChildren()
		{
			yield return _exp;
		}
	}
}
