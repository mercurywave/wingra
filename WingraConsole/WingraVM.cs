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
		string _path;
		internal ORuntime _runtime = new ORuntime();
		WingraProject _proj; // only populated in editor integrated mode!

		public ORuntime Runtime => _runtime;
		internal bool _isDebug;
		internal bool _isTest;
		internal bool _isIDE;
		internal bool _isAsmDebug;

		WingraVM(bool isDebug, bool isTest, bool isIDE, bool isAsmDebug = false)
		{
			_isDebug = isDebug;
			_runtime.Debug = isDebug;
			_isTest = isTest;
			_isIDE = isIDE;
			_isAsmDebug = isAsmDebug;
			//_runtime.hookException = DoDebug;
		}

		public WingraVM(string folder, bool isDebug = false, bool isTest = false, bool isIDE = false, bool isAsmDebug = false) : this(isDebug, isTest, isIDE, isAsmDebug)
		{
			_path = folder;
		}

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

		public async Task<Compiler> CompileAllAsync(WingraSymbols symbols = null)
		{
			var prj = await GetProject();
			return CompileNow(prj, symbols);
		}
		public async Task<WingraProject> InitializeAsync(WingraSymbols symbols = null)
		{
			var prj = await GetProject();
			InitializeNow(prj, symbols);
			return prj;
		}

		public async Task RunMain(WingraProject prj, WingraSymbols symbols = null)
		{
			var start = Stopwatch.GetTimestamp();
			await _runtime.RunMain();
			var complTime = Stopwatch.GetTimestamp();
			WriteToConsole("Run (" + DurationMs(start, complTime) + " ms)");
			if (prj.CheckConfigFlag("runTests"))
				await RunTests(symbols);
		}

		public async Task RunTests(WingraSymbols symbols = null)
		{
			var start = Stopwatch.GetTimestamp();
			await _runtime.RunTests();
			var complTime = Stopwatch.GetTimestamp();
			WriteToConsole("Run Tests (" + DurationMs(start, complTime) + " ms)");
		}

		public async Task<WingraProject> GetProject()
		{
			if (_proj != null)
			{
				await _proj.LoadAllFiles(); // may not be laoded by the editor earlier
				return _proj;
			}
			if (_path != "")
			{
				var cache = new Dictionary<string, WingraProject>();
				var prj = await LoadProject(_path, cache);
				return prj;
			}
			throw new NotImplementedException();
		}

		async Task<WingraProject> LoadProject(string path, Dictionary<string, WingraProject> cache)
		{
			if (cache.ContainsKey(path))
				return cache[path];

			var prj = new WingraProject(path, new CodeFileServer());
			cache.Add(path, prj);
			fileUtils.PreLoadDirectory(path, prj);
			await LoadDependentProjects(prj, path, cache);
			await prj.LoadAllFiles();
			return prj;
		}

		async Task LoadDependentProjects(WingraProject prj, string dir, Dictionary<string, WingraProject> cache)
		{
			var file = fileUtils.CombinePath(dir, "project." + prj.ProjExtension);
			if (!fileUtils.FileExists(file))
				return;
			await prj.LoadConfigProject(file);
			foreach (var path in prj.RequiredPaths)
			{
				var child = await LoadProject(path, cache);
				prj.RequiredProjects.Add(child);
			}
		}

		Compiler CompileNow(WingraProject prj, WingraSymbols symbols = null)
		{
			if (_isTest && symbols == null) symbols = new WingraSymbols();
			_runtime.InjectDynamicLibrary(new IO(this), "IO");
			_runtime.LoadPlugins(prj);
			var compiler = new Compiler(_runtime.StaticMap
				, _isDebug
				, _isTest || prj.CheckConfigFlag("runTests")
				, false
				, _isIDE
				, _isAsmDebug);
			var compl = prj.CompileAll(compiler, symbols);
			//NOTE: add functions to completion match as well
			_runtime.RegisterFiles(compl);
			return compiler;
		}

		void InitializeNow(WingraProject prj, WingraSymbols symbols = null)
		{
			WriteToConsole("Booting...");
			var start = Stopwatch.GetTimestamp();
			var comp = CompileNow(prj, symbols);
			var complTime = Stopwatch.GetTimestamp();

			if (!prj.CompileErrors.Errors.Any())
			{
				WriteToConsole("Compiled! (" + DurationMs(start, complTime) + " ms)");
				start = Stopwatch.GetTimestamp();

				_runtime.Initialize(comp);
				var done = Stopwatch.GetTimestamp();
				WriteToConsole("Initialized! (" + DurationMs(start, done) + " ms)");
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
