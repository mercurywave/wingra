using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Wingra.Interpreter;

namespace Wingra.Transpilers
{
	public class AsmDebug
	{
		WingraCompile _compile;
		WingraSymbols _symbols;
		WingraBuffer _source;

		public AsmDebug(WingraCompile compile, WingraSymbols symbols)
		{
			_compile = compile;
			_symbols = symbols;
		}

		public StringBuilder Output(WingraBuffer source)
		{
			StringBuilder sb = new StringBuilder();
			var asm = _compile.Assemblies.First(a => a.Key == source.Key);
			foreach (var key in AssemblyFile.LoadKeys())
			{
				var func = asm.GetByName(key);
				if (func != null)
					EmitFunction(sb, key, func);
			}
			if(asm.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("//CODE");
				foreach (var pair in asm)
					EmitFunction(sb, pair.Key, pair.Value);
			}
			return sb;
		}

		void EmitFunction(StringBuilder sb, string name, AssemblyCode code)
		{
			sb.AppendLine(name);
			sb.AppendLine(code.DebugVars());
			for (int i = 0; i < code.Count; i++)
			{
				if (i % 100 == 0) sb.AppendLine(AssemblyCode.DebugPrintHeader());
				sb.AppendLine(code.DebugPrint(i, true));
			}
		}
	}
}
