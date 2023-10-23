using Wingra.Parser;
using System;
using System.Collections.Generic;
using System.Text;

namespace Wingra.Interpreter
{
	public class QueryTemplate
	{
		string _code;
		CodeBlock _entryPoint;
		public QueryTemplate(string code) { _code = code; }
		public CodeBlock GetEntryPoint(ORuntime runtime)
		{
			if (_entryPoint != null) return _entryPoint;
			var asm = runtime._compiler.CompileExpression(_code);
			_entryPoint = new CodeBlock(asm);
			return _entryPoint;
		}
	}
	public class TaskTemplate
	{
		string _code;
		CodeBlock _entryPoint;
		public TaskTemplate(string code) { _code = code; }

		public CodeBlock GetEntryPoint(ORuntime runtime)
		{
			if (_entryPoint != null) return _entryPoint;
			var asm = runtime._compiler.CompileStatement(_code);
			_entryPoint = new CodeBlock(asm);
			return _entryPoint;
		}
	}
	public class LambdaQueryTemplate
	{
		string _code;
		CodeBlock _entryPoint;
		public LambdaQueryTemplate(string code) { _code = code; }
		public CodeBlock GetEntryPoint(ORuntime runtime)
		{
			if (_entryPoint != null) return _entryPoint;
			var asm = runtime._compiler.CompileLambda(_code);
			_entryPoint = new CodeBlock(asm);
			return _entryPoint;
		}
		// creates a variable that contains a function pointer to this code
		public Variable GenerateLambda(ORuntime runtime)
			=> new Variable(GetEntryPoint(runtime));
	}
}
