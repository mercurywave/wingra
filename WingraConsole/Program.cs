using System;
using System.Linq;
using System.Threading.Tasks;
using Wingra;
using Wingra.Interpreter;

namespace WingraConsole
{
	class Program
	{
		internal ORuntime _runtime = new ORuntime();
		static async Task Main(string[] args)
		{
			WingraSymbols symbols = new WingraSymbols();
			try
			{
				Console.WriteLine("Load " + Environment.CurrentDirectory);
				var vm = new WingraVM(Environment.CurrentDirectory);
				var prj = await vm.InitializeAsync(symbols);
				if (prj.CompileErrors.Errors.Any())
				{
					Console.WriteLine();
					foreach (var err in prj.CompileErrors.Errors)
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
					Console.WriteLine(prj.CompileErrors.Errors.Count() + " errors");
				}
				else
				{
					await vm.RunMain(prj);
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
			catch (Exception e)
			{
				Console.WriteLine("Unexpected Panic");
				Console.WriteLine(e.ToString());
			}
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
