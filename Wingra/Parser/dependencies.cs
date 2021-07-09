using Wingra.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	class SDeclareSymbol : SStatement
	{
		string _symbol;
		public SDeclareSymbol(int fileLine, RelativeTokenReference[] symbols) : base(fileLine)
		{
			string packed = util.Join(symbols.Select(s => s.Token.Token), " ");
			_symbol = packed.Trim();
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			file.ExportedSymbols.Add(_symbol);
		}
	}

	class SRequireSymbol : SStatement
	{
		string _symbol;
		public SRequireSymbol(int fileLine, RelativeTokenReference[] symbols) : base(fileLine)
		{
			string packed = util.Join(symbols.Select(s => s.Token.Token), " ");
			_symbol = packed.Trim();
		}

		internal override void _EmitAssembly(Compiler compiler, FileAssembler file, FunctionFactory func, int asmStackLevel, ErrorLogger errors, SyntaxNode parent)
		{
			file.RequiredSymbols.Add(_symbol);
		}
	}
}
