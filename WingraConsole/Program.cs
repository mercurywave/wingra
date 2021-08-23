using System;
using System.Linq;
using System.Threading.Tasks;
using Wingra;
using Wingra.Interpreter;
using Wingra.Parser;

namespace WingraConsole
{
	class Program
	{
		static async Task Main(string[] args)
		{
			try
			{
				var parsedArgs = CommandLineArgs.Parse(args);
				var prj = await Loader.LoadProject(Environment.CurrentDirectory);
				if (prj.IsJsExport)
					await ExportJs(prj);
				else
					await RunInterpretted(prj, parsedArgs);
			}
			catch (Exception e)
			{
				Console.WriteLine("Unexpected Panic");
				Console.WriteLine(e.ToString());
			}
		}

		static async Task RunInterpretted(WingraProject prj, CommandLineArgs args)
		{
			var vm = new WingraVM();
			WingraSymbols symbols = new WingraSymbols();
			try
			{
				if (args.Verbose)
					Console.WriteLine("Load " + Environment.CurrentDirectory);
				vm.InitializeNow(prj, symbols, args.Verbose);
				if (prj.CheckForErrors())
					PrintCompilerErrors(prj);
				else
				{
					await vm.RunMain(prj, symbols, args.Verbose);
					await Task.Yield();
					if (vm.HasOpenJobs)
						Console.WriteLine("Main() completed with " + vm.OpenJobs + " open jobs running");
				}
			}
			catch (RuntimeException e)
			{
				Console.WriteLine("Runtime Exception !!!");
				Console.WriteLine(e.Message);
				foreach (var lvl in e.Owner.GetDebugStack().Reverse<Scope>())
				{
					Console.WriteLine(symbols.WhereIsStack(lvl));
					Console.WriteLine(" " + symbols.GetCodeAt(lvl));
				}
				Console.WriteLine(e.ToString());
			}

		}

		static async Task ExportJs(WingraProject prj)
		{
			WingraSymbols symbols = new WingraSymbols();
			var compiler = new Compiler(prj);
			var result = prj.CompileAll(compiler, symbols);
			if (prj.CheckForErrors())
				PrintCompilerErrors(prj);
			else
			{
				var file = prj.CheckConfigString("jsExport");
				var fnName = prj.CheckConfigString("jsFunc", "SETUP");
				var trans = new Wingra.Transpilers.Javascript(result, symbols);
				var sb = trans.Output(fnName);
				if (file == "")
					Console.Write(sb);
				else
				{
					await CodeFileServer.AsyncSaveFile(file, sb);
					Console.WriteLine("Successfully wrote js file: " + file);
				}
			}
		}

		static void PrintCompilerErrors(WingraProject prj)
		{
			Console.WriteLine();
			var errs = prj.GetAllErrors();
			foreach (var err in errs)
			{
				if (err.Buffer != null)
					Console.WriteLine(err.Buffer.ShortFileName + " line " + err.Line);
				if (err.Line >= 0)
				{
					var lines = err.Buffer.GetCompleteLine(err.Line, out _);
					for (int i = 0; i < lines.Count; i++)
					{
						var text = err.Buffer.TextAtLine(err.Line + i);
						Console.WriteLine(" " + text.Replace("\t", RepeatString(" ", WingraBuffer.SpacesToIndent)));
						if (err.Token.HasValue && err.Token.Value.SubLine == i)
						{
							var off = VisualOffset(text, err.Token.Value.Token.LineOffset, WingraBuffer.SpacesToIndent);
							Console.WriteLine(" " + RepeatString(" ", off) + "^");
						}
					}
				}
				Console.WriteLine(err.Text);
				//if (!string.IsNullOrEmpty(err.ExtraText))
				//Console.WriteLine(err.ExtraText);
				Console.WriteLine();
			}
			Console.WriteLine(errs.Count() + " errors");
		}

		static string RepeatString(string str, int count, string delimiter = "")
		{
			if (count == 0) return "";
			var output = str;
			for (int i = 0; i < count - 1; i++)
				output += delimiter + str;
			return output;
		}

		static int VisualOffset(string line, int idx, int tabWidth)
		{
			int off = idx;
			for (int i = 0; i < line.Length && i < idx; i++)
				if (line[i] == '\t')
					off += (tabWidth - 1);
			return off;
		}
	}
}
