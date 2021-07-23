using Wingra.Parser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Wingra.Interpreter
{
	public class ORuntime
	{
		internal VariableTable StaticScope;
		internal Dictionary<string, VariableTable> ScratchScopes = new Dictionary<string, VariableTable>();
		// temporary scratches are stored in LScratch
		public Action<RuntimeException> hookException = null;
		public StaticMapping StaticMap = new StaticMapping(); // used for queries to resolve static references
		internal Malloc Heap = new Malloc();
		FastStack<Job> _jobPool = new FastStack<Job>();
		FastStack<AsyncJob> _bgJobPool = new FastStack<AsyncJob>(4);
		Dictionary<int, AsyncJob> _activeJobs = new Dictionary<int, AsyncJob>();
		int _jobID = 0;
		public bool Debug = false;
		internal bool ShuttingDown = false;
		internal Compiler _compiler;

		List<FileCodeInstance> _filesToInit = new List<FileCodeInstance>();
		Dictionary<string, FileCodeInstance> AllFiles = new Dictionary<string, FileCodeInstance>();

		public ORuntime()
		{
			StaticScope = new VariableTable(Heap);
			SetupStandardHooks();
		}

		public void CheckIn(Job jb)
		{
			if (!Debug) _jobPool.Push(jb);
		}
		public Job CheckOutJob()
		{
			if (_jobPool.IsEmpty) return new Job(this);
			return _jobPool.Pop();
		}

		internal AsyncJob QueueBackground(CodeBlock entrypoint, VariableList capture = null)
		{
			AsyncJob job;
			if (_bgJobPool.IsEmpty) job = new AsyncJob(this);
			else job = _bgJobPool.Pop();
			job.Initialize(entrypoint, _jobID++);
			_activeJobs.Add(job.ID, job);
			job.KickOff(capture);
			return job;
		}
		internal void CheckIn(AsyncJob jb)
		{
			_activeJobs.Remove(jb.ID);
			_bgJobPool.Push(jb);
		}

		internal AsyncJob GetBackgroundJob(int id)
		{
			if (_activeJobs.ContainsKey(id)) return _activeJobs[id];
			return null; // job done
		}
		public bool HasOpenJobs => _activeJobs.Any();
		public int OpenJobs => _activeJobs.Count;

		public void CheckIn(Variable var) => var.Dispose(Heap);

		public void RegisterFiles(AssemblyFile file) => _filesToInit.Add(new FileCodeInstance(file, Heap));

		public void RegisterFiles(WingraCompile comp)
		{
			foreach (var code in comp.Assemblies)
			{
				var fci = new FileCodeInstance(code, Heap);
				_filesToInit.Add(fci);
				AllFiles.Add(fci.Key, fci);
				ScratchScopes[fci.Key] = new VariableTable(Heap);
			}
		}

		DualIndex<string, FileCodeInstance> _exportedSymbols = new DualIndex<string, FileCodeInstance>();
		internal void RegisterExportedSymbol(string symbol, FileCodeInstance fci) => _exportedSymbols.Set(symbol, fci);

		DualIndex<string, FileCodeInstance> _requiredSymbols = new DualIndex<string, FileCodeInstance>();
		internal void RegisterRequiredSymbol(string symbol, FileCodeInstance fci) => _requiredSymbols.Set(symbol, fci);

		internal bool _initialized = false;
		public void Initialize(Compiler dynamicCompiler)
		{
			_compiler = dynamicCompiler;
			List<FileCodeInstance> initOrder = _filesToInit; // already sorted after compile

			foreach (var file in initOrder)
				RunSingleSetupMethod(file.FuncDefFunc);

			foreach (var file in initOrder)
				RunSingleSetupMethod(file.StaticInitFunc);

			foreach (var file in initOrder)
				RunSingleSetupMethod(file.DataGloFunc);

			foreach (var file in initOrder)
				RunSingleSetupMethod(file.StructFunc);

			BuildRegistries();

			foreach (var file in initOrder)
				RunSingleSetupMethod(file.InitFunc);

			_initialized = true;
		}

		public async Task RunMain()
		{
			if (StaticMap.TryGetMainFunc(out var isAsync))
			{
				if (isAsync)
					await RunAsync(new TaskTemplate("await $Main()"));
				else
					Run(new TaskTemplate("$Main()"));
			}
		}
		public async Task RunTests()
		{
			if (StaticMap.TryGetTestFunc(out var isAsync))
			{
				if (isAsync)
					await RunAsync(new TaskTemplate("await $TestMain()"));
				else
					Run(new TaskTemplate("$TestMain()"));
			}
		}

		MapSet<string, Variable> _registries = new MapSet<string, Variable>();
		internal void RegisterRecord(string key, Variable record) => _registries.Set(key, record);
		void BuildRegistries() // TODO: delete - I didn't go down this route for data
		{
			if (_registries.IsEmpty) return;
			var glo = MakeGlobalBranch();
			StaticScope.Set("Registry", glo);
			foreach (var key in _registries.Keys())
			{
				var list = MakeGlobalBranch();
				glo.SetChild(new Variable(key), list, Heap);
				int idx = 0;
				foreach (var val in _registries.Values(key))
					list.SetChild(new Variable(idx++), val, Heap);
			}
		}

		void SetupStandardHooks()
		{
			StandardLib.Setup(this);
		}

		void RunSingleSetupMethod(CodeBlock code)
		{
			if (code == null) return;
			using (MakeTempJob(out var jb, code))
				jb.RunToCompletion();
		}

		public Variable LoadStaticGlobal(string path)
			=> LoadStaticGlobal(StaticMapping.SplitPath(path));
		Variable LoadStaticGlobal(string[] split)
		{
			var target = StaticScope.GetVarOrNull(split[0]);
			if (target == null)
				throw new RuntimeException("could not find static path " + util.Join(split, "."));
			for (int i = 1; i < split.Length; i++)
			{
				var next = target.Value.TryGetChild(split[i]);
				if (next.HasValue) target = next.Value;
				else throw new RuntimeException("could not find static path " + util.Join(split, "."));
			}
			return target.Value;
		}
		public Variable LoadStaticFromFile(string path, string fileKey)
			=> LoadStaticFromFile(StaticMapping.SplitPath(path), fileKey);
		Variable LoadStaticFromFile(string[] split, string fileKey)
		{
			if (string.IsNullOrEmpty(fileKey)) throw new Exception("file not found?");
			var file = AllFiles[fileKey];
			var name = StaticMapping.JoinPath(split);
			var lamb = new Variable(file.NamedFunctions[name], Heap);
			return lamb;
		}

		public Variable LoadStatic(string absPath)
		{
			var split = StaticMapping.SplitAbsPath(absPath, out var type, out var fileKey);
			if (type == StaticMapping.FILE_ABS) LoadStaticFromFile(split, fileKey);
			else if (type == StaticMapping.DATA_ABS) return LoadStaticGlobal(split);
			throw new Exception("invalid path");
		}
		public void InjectStaticVar(string path, Variable var, eStaticType type, string fileKey, int fileLine)
		{
			SExpressionComponent exp = null;
			if (var.IsBool || var.IsString || var.IsInt || var.IsFloat || var.IsNull)
				exp = new SCompileConst(var);
			StaticMap.AddStaticGlobal(path, type, fileKey, fileLine, null, exp);
			LoadStaticVar(path, var);
		}

		internal void LoadStaticVar(string path, Variable var)
		{
			var split = StaticMapping.SplitPath(path);
			if (split.Length == 1)
			{
				var.FlagAsData();
				StaticScope.Set(split[0], var);
				return;
			}
			if (!StaticScope.Has(split[0]))
				StaticScope.Set(split[0], MakeGlobalBranch());
			var target = StaticScope.Get(split[0]);
			for (int i = 1; i < split.Length - 1; i++)
			{
				var test = target.TryGetChild(split[i]);
				if (!test.HasValue)
				{
					var node = MakeGlobalBranch();
					target.SetChild(new Variable(split[i]), node, Heap);
					target = node;
				}
				else target = test.Value;
			}
			var last = split[split.Length - 1];
			if (target.HasChildKey(last)) throw new Exception("doubled static assignment " + path);
			target.SetChild(new Variable(last), var, Heap);
		}

		Variable MakeGlobalBranch()
		{
			var inner = new DObject(4);
			var glo = new Variable(inner, Heap);
			glo.FlagAsGlobal();
			return glo;
		}

		public void RaiseError(RuntimeException ex)
		{
			if (hookException == null) throw ex;
			hookException(ex);
		}

		#region library stuff
		public void InjectDynamicLibrary(Type type, string libraryName = "")
			=> ExternalCalls.AddDynamicLibrary(this, type, libraryName);
		public void InjectDynamicLibrary(object obj, string libraryName = "")
			=> ExternalCalls.AddDynamicLibrary(this, obj, libraryName);
		public void InjectExternalCall(MethodInfo meth, object host = null, string function = "", string libraryName = "")
			=> ExternalCalls.AddLibFunction(this, meth, host, function, libraryName);

		public void LoadPlugin(string absPath)
		{
			ExternalCalls.LoadPlugin(this, absPath);
		}

		public void InjectExternalCall(Action<Job, Variable?> act, string name, string path = "")
		{
			var lamb = new ExternalFuncPointer(act);
			InjectStaticVar(ExternalCalls.MakeFuncPath(path, name), new Variable(lamb), eStaticType.External, "", -1);
		}
		public void InjectExternalAsyncCall(Func<Job, Variable?, Task> act, string name, string path = "")
		{
			var lamb = new ExternalAsyncFuncPointer(act);
			InjectStaticVar(ExternalCalls.MakeFuncPath(path, name), new Variable(lamb), eStaticType.External, "", -1);
		}

		#endregion

		#region query runners

		ODisposable MakeTempJob(out Job jb, CodeBlock code)
		{
			var temp = CheckOutJob();
			temp.Initialize(code);
			jb = temp;
			return new ODisposable(() => CheckIn(temp));
		}

		Variable? _RunExp(CodeBlock code)
		{
			using (MakeTempJob(out var job, code))
				return job.RunExpression();
		}
		void _RunTask(CodeBlock code)
		{
			using (MakeTempJob(out var job, code))
				job.RunToCompletion();
		}
		async Task _RunAsyncTask(CodeBlock code)
		{
			var job = QueueBackground(code);
			await job.RunToCompletionAsync();
			// async job checks itself back in
		}
		Variable? _RunExp(CodeBlock code, string name1, Variable value1)
		{
			using (MakeTempJob(out var job, code))
			{
				job.InjectLocal(name1, value1);
				return job.RunExpression();
			}
		}
		void _RunTask(CodeBlock code, string name1, Variable value1)
		{
			using (MakeTempJob(out var job, code))
			{
				job.InjectLocal(name1, value1);
				job.RunToCompletion();
			}
		}
		Variable? _RunExp(CodeBlock code, string name1, Variable value1, string name2, Variable value2)
		{
			using (MakeTempJob(out var job, code))
			{
				job.InjectLocal(name1, value1);
				job.InjectLocal(name2, value2);
				return job.RunExpression();
			}
		}
		void _RunTask(CodeBlock code, string name1, Variable value1, string name2, Variable value2)
		{
			using (MakeTempJob(out var job, code))
			{
				job.InjectLocal(name1, value1);
				job.InjectLocal(name2, value2);
				job.RunToCompletion();
			}
		}

		Job _RunBegin(CodeBlock code) => new Job(this, code);
		Job _RunBegin(CodeBlock code, string name1, Variable value1)
		{
			using (MakeTempJob(out var job, code))
			{
				job.InjectLocal(name1, value1);
				return job;
			}
		}
		Job _RunBegin(CodeBlock code, string name1, Variable value1, string name2, Variable value2)
		{
			using (MakeTempJob(out var job, code))
			{
				job.InjectLocal(name1, value1);
				job.InjectLocal(name2, value2);
				return job;
			}
		}

		public Variable? Run(QueryTemplate qt)
			=> _RunExp(qt.GetEntryPoint(this));
		public Variable? Run(QueryTemplate qt, string name1, Variable value1)
			=> _RunExp(qt.GetEntryPoint(this), name1, value1);
		public Variable? Run(QueryTemplate qt, string name1, Variable value1, string name2, Variable value2)
			=> _RunExp(qt.GetEntryPoint(this), name1, value1, name2, value2);

		public void Run(TaskTemplate qt)
			=> _RunTask(qt.GetEntryPoint(this));
		public void Run(TaskTemplate qt, string name1, Variable value1)
			=> _RunTask(qt.GetEntryPoint(this), name1, value1);
		public void Run(TaskTemplate qt, string name1, Variable value1, string name2, Variable value2)
			=> _RunTask(qt.GetEntryPoint(this), name1, value1, name2, value2);

		public Job BeginRun(QueryTemplate qt) => _RunBegin(qt.GetEntryPoint(this));
		public Job BeginRun(QueryTemplate qt, string name1, Variable value1)
			=> _RunBegin(qt.GetEntryPoint(this), name1, value1);
		public Job BeginRun(QueryTemplate qt, string name1, Variable value1, string name2, Variable value2)
			=> _RunBegin(qt.GetEntryPoint(this), name1, value1, name2, value2);

		public Job BeginRun(TaskTemplate tt) => _RunBegin(tt.GetEntryPoint(this));
		public Job BeginRun(TaskTemplate tt, string name1, Variable value1)
			=> _RunBegin(tt.GetEntryPoint(this), name1, value1);
		public Job BeginRun(TaskTemplate tt, string name1, Variable value1, string name2, Variable value2)
			=> _RunBegin(tt.GetEntryPoint(this), name1, value1, name2, value2);

		public async Task RunAsync(TaskTemplate qt)
			=> await _RunAsyncTask(qt.GetEntryPoint(this));
		#endregion

		public void ShutDown()
		{
			// I don't think I want to start nuking variables or anything
			// for now, this just helps async jobs shut down
			ShuttingDown = true;
		}
	}

	public class FileCodeInstance : Dictionary<string, CodeBlock>
	{
		public string Name;
		public string Key;
		internal Dictionary<string, CodeBlock> NamedFunctions = new Dictionary<string, CodeBlock>(); // populated during initialize
		internal VariableTable Constants;
		internal CodeBlock FuncDefFunc = null;
		internal CodeBlock DataGloFunc = null;
		internal CodeBlock StructFunc = null; // register exported symbols
		internal CodeBlock StaticInitFunc = null;
		internal CodeBlock InitFunc = null; // run user code in file scope / define structures

		public enum eState { Uninitialized, Processing, Ready }
		public eState State = eState.Uninitialized;

		internal FileCodeInstance(AssemblyFile file, Malloc heap)
		{
			Name = file.Name;
			Key = file.Key;
			if (file.FunctionFunc != null)
				FuncDefFunc = new CodeBlock(file.FunctionFunc, this);
			if (file.RegistryFunc != null)
				DataGloFunc = new CodeBlock(file.RegistryFunc, this);
			if (file.StructureFunc != null)
				StructFunc = new CodeBlock(file.StructureFunc, this);
			if (file.StaticInitFunc != null)
				StaticInitFunc = new CodeBlock(file.StaticInitFunc, this);
			if (file.InitFunc != null)
				InitFunc = new CodeBlock(file.InitFunc, this);
			foreach (var pair in file)
				this.Add(pair.Key, new CodeBlock(pair.Value, this));
			Constants = new VariableTable(heap);
		}

		public string FindFunctionName(CodeBlock code)
		{
			if (code == FuncDefFunc) return AssemblyFile.FUNCTION_ROUTINE;
			if (code == DataGloFunc) return AssemblyFile.REGISTRY_ROUTINE;
			if (code == StructFunc) return AssemblyFile.STRUCT_ROUTINE;
			if (code == StaticInitFunc) return AssemblyFile.STATIC_INIT_ROUTINE;
			if (code == InitFunc) return AssemblyFile.INIT_ROUTINE;
			foreach (var pair in this)
				if (pair.Value == code)
					return pair.Key;
			return "[function does not exist in " + Name + "]";
		}

		public override string ToString()
		{
			return "File: " + Name + " (" + Key + ")";
		}

		internal void SaveConstant(string path, Variable val, Malloc heap)
		{
			var split = StaticMapping.SplitPath(path);
			if (split.Length == 1)
				Constants.Set(path, val);
			else
			{
				var node = Constants.GetOrReserve(split[0]);
				if (!node.HasValue)
				{
					node = new Variable(new DObject(4), heap);
					Constants.Set(split[0], node);
				}
				for (int i = 1; i < split.Length; i++)
				{
					var key = split[i];
					if (!node.HasChildKey(key))
					{
						Variable child;
						if (i == split.Length - 1)
							child = val;
						else
							child = new Variable(new DObject(4), heap);
						node.SetChild(new Variable(key), child, heap);
					}
					node = node.TryGetChild(key).Value;
				}
			}
		}
	}
	public class RuntimeException : Exception
	{
		public Job Owner;
		public RuntimeException(string message) : base(message) { }
		public RuntimeException(string message, Job owner) : this(message) { Owner = owner; }
	}
}
