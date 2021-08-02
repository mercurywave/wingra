using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Parser
{
	class SData : SStatement
	{
		// child dims off a record need some of the features of records
		// so the functionality is baked into dim directly instead
		SExpressionComponent _exp;
		SStaticDeclaredPath _path;
		public SData(int fileLine, SExpressionComponent exp, SStaticDeclaredPath path = null) : base(fileLine)
		{
			_exp = exp;
			_path = path;
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			_path?.Reserve(context.Comp, context.FileKey, context.FileLine);
			base.OnAddedToTree(context);
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var header = file.StructureRoutine;
			_exp.EmitAssembly(compiler, file, header, 0, errors, this);
			if (_path == null)
				header.Add(0, eAsmCommand.StoreToData);
			else
				_path.EmitSave(compiler, file, header, 0, errors, this);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _exp;
		}
	}

	class SLibrary : SScopeStatement
	{
		SStaticDeclaredPath _path;
		public SLibrary(int fileLine, SStaticDeclaredPath path) : base(fileLine)
		{
			_path = path;
		}
		internal override void OnAddedToTree(ParseContext context)
		{
			_path.Reserve(context.Comp, context.FileKey, context.FileLine);
			context.Scope.RegisterDeclaringNamespace(_path.ToText(), true, true);
			context.Scope.RegisterUsingNamespaceRef(_path.ToText(), true);
			base.OnAddedToTree(context);
		}
		public override bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			if (LineParser.TryParseLibrary(context, currLine, out node, out usedTokens))
				return true;
			return base.TryParseChild(context, currLine, out node, out usedTokens);
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			EmitChildren(compiler, file, func, asmStackLevel, errors);
		}
	}

	class SConst : SStatement
	{
		SExpressionComponent _exp;
		SStaticDeclaredPath _path;
		public SConst(int fileLine, SStaticDeclaredPath path, SExpressionComponent exp) : base(fileLine)
		{
			_path = path;
			_exp = exp;
		}
		internal override void OnAddedToTree(ParseContext context)
		{
			_path.Reserve(context.Comp, context.FileKey, context.FileLine, _exp);
			base.OnAddedToTree(context);
		}
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var header = file.StaticInitRoutine;
			_exp.EmitAssembly(compiler, file, header, 0, errors, this);
			_path.EmitSave(compiler, file, header, 0, errors, this);
		}
		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _exp;
		}
	}

	class SEnumType : SStatement, IHaveChildScope
	{
		SStaticDeclaredPath _path;
		List<SEnumValue> _children = new List<SEnumValue>();
		public SEnumType(int fileLine, SStaticDeclaredPath path) : base(fileLine)
		{
			_path = path;
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			_path.Reserve(context.Comp, context.FileKey, context.FileLine);
			context.Scope.RegisterDeclaringNamespace(_path.ToText(), true, false);
			context.Scope.RegisterUsingNamespaceRef(_path.ToText(), true);
			base.OnAddedToTree(context);
		}

		public void AddChild(SyntaxNode node)
			=> _children.Add(node as SEnumValue);
		public bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
			=> LineParser.TryParseEnumChild(context, currLine, out node, out usedTokens);
		public void AddBlankLine() { }

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			var header = file.StructureRoutine;
			foreach (var val in _children)
				val.EmitEnum(compiler, file, header, asmStackLevel, errors);
		}
	}

	class SEnumValue : SStatement
	{
		SStaticDeclaredPath _path;
		SExpressionComponent _value;

		public SEnumValue(int fileLine, string declaredParent, SIdentifier ident, SExpressionComponent value = null) : base(fileLine)
		{
			_path = new SStaticDeclaredPath(eStaticType.Data, new RelativeTokenReference[] { ident._source }, declaredParent);
			_value = value;
		}

		internal override void OnAddedToTree(ParseContext context)
		{
			_path.Reserve(context.Comp, context.FileKey, context.FileLine);
			base.OnAddedToTree(context);
		}

		public override IEnumerable<SExpressionComponent> IterExpressions()
		{
			yield return _path;
			if (_value != null) yield return _value;
		}

		internal void EmitEnum(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors)
		{
			if (_value == null)
				func.Add(asmStackLevel, eAsmCommand.PushNull);
			else
				_value.EmitAssembly(compiler, file, func, asmStackLevel, errors, this);
			_path.EmitSaveEnum(compiler, file, func, asmStackLevel);
		}
	}

	class STextData : SExpressionComponent, IHaveChildScope
	{
		List<STextDataLine> _lines = new List<STextDataLine>();
		public void AddChild(SyntaxNode node)
		{
			_lines.Add(node as STextDataLine);
		}

		public bool TryParseChild(ParseContext context, RelativeTokenReference[] currLine, out SyntaxNode node, out int usedTokens)
		{
			node = new STextDataLine(context.Buffer.TextAtLine(context.FileLine).Trim());
			usedTokens = currLine.Length;
			return true;
		}
		public void AddBlankLine()
		{
			_lines.Add(new STextDataLine(""));
		}

		internal override void EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			func.Add(asmStackLevel, eAsmCommand.DimArray, _lines.Count);
			for (int i = 0; i < _lines.Count; i++)
			{
				var line = _lines[i];
				func.Add(asmStackLevel, eAsmCommand.PushString, line._text);
				func.Add(asmStackLevel, eAsmCommand.DimSetInt, i);
			}
		}
	}
	class STextDataLine : SyntaxNode
	{
		internal string _text;
		public STextDataLine(string text) { _text = text; }
	}
}
