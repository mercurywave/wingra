using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wingra;
using Wingra.Interpreter;
using Wingra.Parser;

namespace WingraConsole
{
	class WingraVM
	{
		internal ORuntime _runtime = new ORuntime();

		public ORuntime Runtime => _runtime;

		public bool HasOpenJobs => _runtime.HasOpenJobs;
		public int OpenJobs => _runtime.OpenJobs;

		//public async Task DoTrace(Wingra.Interpreter.Trace trace, WingraSymbols symbols = null)
		//{
		//	if (symbols == null)
		//	{
		//		symbols = new WingraSymbols();
		//		var prj = await GetProject();
		//		prj.CompileAll(true, false, false, symbols);
		//	}
		//	STraceViewer.Show(trace, symbols);
		//}

		public async Task RunMain(WingraProject prj, WingraSymbols symbols = null, bool verbose = false)
		{
			var start = Stopwatch.GetTimestamp();
			await _runtime.RunMain();
			var complTime = Stopwatch.GetTimestamp();
			if (verbose) WriteToConsole("Run (" + DurationMs(start, complTime) + " ms)");
			if (prj.DoRunTests)
				await RunTests(symbols, verbose);
		}

		public async Task RunTests(WingraSymbols symbols = null, bool verbose = false)
		{
			var start = Stopwatch.GetTimestamp();
			await _runtime.RunTests();
			var complTime = Stopwatch.GetTimestamp();
			if (verbose) WriteToConsole("Run Tests (" + DurationMs(start, complTime) + " ms)");
		}

		internal Compiler CompileNow(WingraProject prj, WingraSymbols symbols = null)
		{
			if (prj.DoRunTests && symbols == null) symbols = new WingraSymbols();
			_runtime.InjectDynamicLibrary(new IO(this), "IO");
			_runtime.LoadPlugins(prj);
			var compiler = new Compiler(prj, _runtime.StaticMap);
			var compl = prj.CompileAll(compiler, symbols);
			//NOTE: add functions to completion match as well
			_runtime.RegisterFiles(compl);
			return compiler;
		}

		internal void InitializeNow(WingraProject prj, WingraSymbols symbols = null, bool verbose = false)
		{
			if (verbose) WriteToConsole("Booting...");
			var start = Stopwatch.GetTimestamp();
			var comp = CompileNow(prj, symbols);
			var complTime = Stopwatch.GetTimestamp();

			if (!prj.CheckForErrors())
			{
				if (verbose) WriteToConsole("Compiled! (" + DurationMs(start, complTime) + " ms)");
				start = Stopwatch.GetTimestamp();

				_runtime.Initialize(comp);
				var done = Stopwatch.GetTimestamp();
				if (verbose) WriteToConsole("Initialized! (" + DurationMs(start, done) + " ms)");
			}
		}

		public void Run(string code) => Run(new TaskTemplate(code));
		public void Run(TaskTemplate code) => _runtime.Run(code);
		public void Run(TaskTemplate code, string var1, Variable value1)
			=> _runtime.Run(code, var1, value1);
		public void Run(TaskTemplate code, string var1, Variable value1, string var2, Variable value2)
			=> _runtime.Run(code, var1, value1, var2, value2);

		public Variable? RunQuery(string code) => RunQuery(new QueryTemplate(code));
		public Variable? RunQuery(QueryTemplate template)
			=> Runtime.Run(template);
		public Variable? RunQuery(QueryTemplate template, string var1, Variable value1)
			=> Runtime.Run(template, var1, value1);

		//public List<Variable> RunQueryList(string code)
		//	=> RunQueryList(new QueryTemplate(code));
		//public List<Variable> RunQueryList(QueryTemplate template)
		//	=> Runtime.RunList(template);

		public string RunQueryString(string code)
			=> RunQueryString(new QueryTemplate(code));
		public string RunQueryString(QueryTemplate code)
			=> Runtime.Run(code).Value.AsString();
		public string RunQueryString(QueryTemplate code, string var1, Variable value1)
			=> Runtime.Run(code, var1, value1).Value.AsString();
		public string RunQueryString(QueryTemplate code, string var1, Variable value1, string var2, Variable value2)
			=> Runtime.Run(code, var1, value1, var2, value2).Value.AsString();

		public int RunQueryInt(string code)
			=> RunQueryInt(new QueryTemplate(code));
		public int RunQueryInt(QueryTemplate code)
			=> Runtime.Run(code).Value.AsInt();
		public int RunQueryInt(QueryTemplate code, string var1, Variable value1)
			=> Runtime.Run(code, var1, value1).Value.AsInt();
		public int RunQueryInt(QueryTemplate code, string var1, Variable value1, string var2, Variable value2)
			=> Runtime.Run(code, var1, value1, var2, value2).Value.AsInt();

		public bool RunQueryBool(string code)
			=> RunQueryBool(new QueryTemplate(code));
		public bool RunQueryBool(QueryTemplate code)
			=> Runtime.Run(code).Value.AsBool();
		public bool RunQueryBool(QueryTemplate code, string var1, Variable value1)
			=> Runtime.Run(code, var1, value1).Value.AsBool();
		public bool RunQueryBool(QueryTemplate code, string var1, Variable value1, string var2, Variable value2)
			=> Runtime.Run(code, var1, value1, var2, value2).Value.AsBool();

		public void WriteToConsole(string text) => Console.WriteLine(text ?? "");

		public static double DurationMs(long start, long end) => 1000.0 * (end - start) / Stopwatch.Frequency;
		public static long MsToTicks(long ms) => ms * Stopwatch.Frequency / 1000;


		public class IO
		{
			WingraVM _vm;
			public IO(WingraVM vm) { _vm = vm; }

			public void Write(string text) => _vm.WriteToConsole(text);
			public void Log(Variable var)
			{
				_vm.WriteToConsole(var.GetValueString());
				_vm.Runtime.CheckIn(var);
			}
			public void DebugLog(Variable var)
			{
				_vm.WriteToConsole(var.ToString());
				_vm.Runtime.CheckIn(var);
			}
		}
	}
}
