using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	static class LCompiler
	{
		public static void Setup(ORuntime runtime, Compiler compiler)
		{
			runtime.InjectStaticVar("Compiler.IsTest", new Variable(compiler._isTest), eStaticType.External, "", -1);
			runtime.InjectStaticVar("Compiler.IsDebug", new Variable(compiler._isDebug), eStaticType.External, "", -1);
			runtime.InjectStaticVar("Compiler.IsSuggestion", new Variable(compiler._isSuggestion), eStaticType.External, "", -1);
			runtime.InjectStaticVar("Compiler.IsIDE", new Variable(compiler._isIDE), eStaticType.External, "", -1);
			runtime.InjectStaticVar("Compiler.IsBootstrap", new Variable(compiler._isBootstrap), eStaticType.External, "", -1);

			runtime.InjectExternalCall((j, t) =>
			{
				j.PassReturn(new Variable(compiler._buffLine));
			}, "GetFileLine", "Compiler");

			runtime.InjectExternalCall((j, t) =>
			{
				j.PassReturn(new Variable(compiler._lastBuffer.ShortFileName));
			}, "GetFileName", "Compiler");

			runtime.InjectExternalCall((j, t) =>
			{
				j.AssertPassingParams(1);
				var code = j.GetPassingParam(0).AsString();
				var qt = new QueryTemplate(code);
				var ret = j.Runtime.Run(qt) ?? new Variable();
				j.PassReturn(ret);
			}, "Eval", "Compiler");

			runtime.InjectDynamicLibrary(new MacroHelp(runtime), "MacroHelp");
		}
	}

	class SMacroDef : SScopeStatement
	{
		string _name;
		public SMacroDef(int fileline, string name) : base(fileline) { _name = name; }
		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			// this embeds the code in a ad-hoc file, not the normal one
			EmitChildren(compiler, file, func, asmStackLevel, errors);
		}
	}
	class MacroHelp
	{
		ORuntime _runtime;
		public MacroHelp(ORuntime runtime) { _runtime = runtime; }

		public string Unindent(string text)
		{
			if (text == "") return "";
			if (text[0] == '\t' || text[0] == ' ') return text.Substring(1, text.Length - 1);
			return text;
		}
		public void UnindentAll(Variable code)
		{
			foreach(var key in code.ChildKeys().ToArray())
			{
				var line = code.TryGetChild(key);
				if (!line.HasValue) continue;
				var text = line.Value.AsString();
				SplitIndent(text, out var tabs, out var content);
				if(tabs.Length > 0) tabs = tabs.Substring(1);
				code.SetChild(key, new Variable(tabs + content), _runtime.Heap);
			}
		}

		public string Escape(string str)
		{
			return str.Replace("\"","\\\"");
		}

		static void SplitIndent(string code, out string first, out string trail)
		{
			var lex = new LexLine(code, WingraBuffer.SpacesToIndent);
			first = lex.IsEmpty ? code : util.BoundedSubstr(code, 0, lex.Tokens[0].LineOffset);
			trail = lex.IsEmpty ? code : util.BoundedSubstr(code, lex.Tokens[0].LineOffset, code.Length); // lazy
		}
	}
}
