using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Wingra.Interpreter
{
	[AttributeUsage(AttributeTargets.Class)]
	public class WingraLibrary : Attribute
	{
		internal string Path;
		public WingraLibrary(string path)
		{
			Path = path;
		}
	}

	[AttributeUsage(AttributeTargets.Class)]
	public class WingraLibrarySetup : Attribute
	{
		// The class is expected to have a function like this:
		// public static void WingraInit(ORuntime run)
		public WingraLibrarySetup() { }
	}

	[AttributeUsage(AttributeTargets.Method)]
	public class WingraMethod : Attribute
	{
		// the first parameter of this function is passed in from wingra via obj.$Func()
		public WingraMethod() { }
	}

	public static class ExternalCalls
	{
		public static void LoadPlugin(ORuntime runtime, string path)
		{
			var asm = Assembly.LoadFrom(path);
			LoadAssembly(runtime, asm);
		}
		public static void LoadAssembly(ORuntime runtime, Assembly assembly)
		{
			var types = assembly.GetTypes();
			foreach (var t in types)
			{
				var setupClass = t.GetTypeInfo().GetCustomAttribute<WingraLibrarySetup>();
				if (setupClass != null)
				{
					var f = t.GetMethod("WingraInit");
					if (f != null)
						f.Invoke(null, new object[] { runtime });
				}
				var classAttr = t.GetTypeInfo().GetCustomAttribute<WingraLibrary>();
				if (classAttr != null)
					AddDynamicLibrary(runtime, t, classAttr.Path);
			}
		}
		//adds public static methods
		public static void AddDynamicLibrary(ORuntime runtime, Type type, string libraryPath = "")
		{
			var calls = type.GetRuntimeMethods().Where(f => f.IsStatic && f.IsPublic);
			ParseReflectionHelper(runtime, libraryPath, calls, null);
		}

		public static void AddDynamicLibrary(ORuntime runtime, object obj, string libraryPath = "")
		{
			var type = obj.GetType();
			var calls = type.GetRuntimeMethods().Where(f => !f.IsStatic && f.IsPublic && f.DeclaringType == type);
			ParseReflectionHelper(runtime, libraryPath, calls, obj);
		}

		public static string MakeFuncPath(string path, string name) => path == "" ? name : path + "." + name;
		public static void AddLibFunction(ORuntime runtime, MethodInfo meth, object host = null, string function = "", string libraryName = "")
		{
			if (function == "") function = meth.Name;
			ParseReflectionHelper(runtime, MakeFuncPath(libraryName, function), new List<MethodInfo>() { meth }, host);
		}

		enum eConvert { none, str, integer, boolean, single, extObj }
		static private void ParseReflectionHelper(ORuntime runtime, string path, IEnumerable<MethodInfo> calls, object host)
		{
			foreach (var meth in calls)
			{
				ParseMethod(meth, out var _retConv, out var _convArr, out var isAsync, out var isMethod);

				if (isAsync)
				{
					var lamb = new ExternalAsyncFuncPointer(async (j, t) =>
					{
						// we have to wrap traps for this in a bunch of places because Method.Invoke seems to confuse it
						try
						{
							await RunParseMethodAsync(j, host, meth, _retConv, _convArr, isMethod, t);
						}
						catch (CatchableError e)
						{
							j.ThrowObject(e.Contents);
						}
					});
					runtime.InjectStaticVar(MakeFuncPath(path, meth.Name), new Variable(lamb), Parser.eStaticType.External, "", -1);
				}
				else
				{
					var lamb = new ExternalFuncPointer((j, t) =>
					{
						RunParseMethod(j, host, meth, _retConv, _convArr, isMethod, t);
					});
					runtime.InjectStaticVar(MakeFuncPath(path, meth.Name), new Variable(lamb), Parser.eStaticType.External, "", -1);
				}
			}
		}

		private static void ParseMethod(MethodInfo meth, out eConvert? _retConv, out eConvert[] _convArr, out bool isAsync, out bool isMethod)
		{
			isMethod = (meth.GetCustomAttribute<WingraMethod>() != null);
			List<eConvert> _convList = new List<eConvert>();
			_retConv = null;
			isAsync = false;
			foreach (var p in meth.GetParameters())
			{
				if (p.ParameterType == typeof(string))
					_convList.Add(eConvert.str);
				else if (p.ParameterType == typeof(int))
					_convList.Add(eConvert.integer);
				else if (p.ParameterType == typeof(bool))
					_convList.Add(eConvert.boolean);
				else if (p.ParameterType == typeof(float))
					_convList.Add(eConvert.single);
				else if (p.ParameterType == typeof(Variable))
					_convList.Add(eConvert.none);
				else
					_convList.Add(eConvert.extObj);
			}
			_convArr = _convList.ToArray();
			if (meth.ReturnType != null)
			{
				if (meth.ReturnType == typeof(string))
					_retConv = eConvert.str;
				else if (meth.ReturnType == typeof(int))
					_retConv = eConvert.integer;
				else if (meth.ReturnType == typeof(bool))
					_retConv = eConvert.boolean;
				else if (meth.ReturnType == typeof(float))
					_retConv = eConvert.single;
				else if (typeof(Variable).IsAssignableFrom(meth.ReturnType))
					_retConv = eConvert.none;
				else if (meth.ReturnType == typeof(Task))
				{
					isAsync = true;
				}
				else if (meth.ReturnType == typeof(Task<string>))
				{
					isAsync = true;
					_retConv = eConvert.str;
				}
				else if (meth.ReturnType == typeof(Task<int>))
				{
					isAsync = true;
					_retConv = eConvert.integer;
				}
				else if (meth.ReturnType == typeof(Task<bool>))
				{
					isAsync = true;
					_retConv = eConvert.boolean;
				}
				else if (meth.ReturnType == typeof(Task<float>))
				{
					isAsync = true;
					_retConv = eConvert.single;
				}
				else if (meth.ReturnType == typeof(Task<Variable>))
				{
					isAsync = true;
					_retConv = eConvert.none;
				}
				else _retConv = eConvert.extObj;
			}
		}

		private static void RunParseMethod(Job j, object host, MethodInfo meth, eConvert? _retConv, eConvert[] _convArr, bool isMethod, Variable? thisVar)
		{
			List<object> inputs = new List<object>(j.TotalParamsPassing);
			GetInputs(j, _convArr, inputs, isMethod, thisVar);
			var ret = meth.Invoke(host, inputs.ToArray());
			if (_retConv.HasValue)
			{
				if (_retConv == eConvert.none)
					j.PassReturn((Variable)ret);
				else if (_retConv == eConvert.integer)
					j.PassReturn(new Variable((int)ret));
				else if (_retConv == eConvert.boolean)
					j.PassReturn(new Variable((bool)ret));
				else if (_retConv == eConvert.single)
					j.PassReturn(new Variable((float)ret));
				else if (_retConv == eConvert.str)
					j.PassReturn(new Variable((string)ret));
				else
					j.PassReturn(Variable.FromExternalObject(ret, j.Heap));
			}
			else j.ReturnNothing();
		}

		private static void GetInputs(Job j, eConvert[] _convArr, List<object> inputs, bool isMethod, Variable? thisVar)
		{
			if (isMethod)
				inputs.Add(ConvertWingraInputToObj(thisVar.Value, _convArr[0]));
			for (int i = 0; i < j.TotalParamsPassing; i++)
			{
				var pass = j.GetPassingParam(i);
				var conv = _convArr[i + (isMethod ? 1 : 0)];
				object obj = ConvertWingraInputToObj(pass, conv);
				inputs.Add(obj);
			}
		}

		private static object ConvertWingraInputToObj(Variable pass, eConvert conv)
		{
			try
			{
				object obj;
				if (conv == eConvert.integer)
					obj = pass.AsInt();
				else if (conv == eConvert.boolean)
					obj = pass.AsBool();
				else if (conv == eConvert.single)
					obj = pass.AsFloat();
				else if (conv == eConvert.str)
					obj = pass.AsString();
				else if (conv == eConvert.extObj)
					obj = pass.GetExternalContents();
				else obj = pass;
				return obj;
			}
			catch (Exception e)
			{ throw new RuntimeException("extern parameter mismatch - " + e.ToString()); }
		}

		private static async Task RunParseMethodAsync(Job j, object host, MethodInfo meth, eConvert? _retConv, eConvert[] _convArr, bool isMethod, Variable? thisVar)
		{
			List<object> inputs = new List<object>(j.TotalParamsPassing);
			GetInputs(j, _convArr, inputs, isMethod, thisVar);
			var tsk = meth.Invoke(host, inputs.ToArray());
			if (_retConv.HasValue)
			{
				if (_retConv == eConvert.none)
				{
					var task = (Task<Variable>)tsk;
					if (task.Exception != null)
						throw task.Exception.InnerException;
					var ret = await task;
					j.PassReturn((Variable)ret);
				}
				else if (_retConv == eConvert.integer)
				{
					var task = (Task<int>)tsk;
					if (task.Exception != null)
						throw task.Exception.InnerException;
					var ret = await task;
					j.PassReturn(new Variable((int)ret));
				}
				else if (_retConv == eConvert.boolean)
				{
					var task = (Task<bool>)tsk;
					if (task.Exception != null)
						throw task.Exception.InnerException;
					var ret = await task;
					j.PassReturn(new Variable((bool)ret));
				}
				else if (_retConv == eConvert.single)
				{
					var task = (Task<float>)tsk;
					if (task.Exception != null)
						throw task.Exception.InnerException;
					var ret = await task;
					j.PassReturn(new Variable((float)ret));
				}
				else if (_retConv == eConvert.str)
				{
					var task = (Task<string>)tsk;
					if (task.Exception != null)
						throw task.Exception.InnerException;
					var ret = await task;
					j.PassReturn(new Variable((string)ret));
				}
				else if (_retConv == eConvert.extObj)
				{
					var task = (Task<object>)tsk;
					if (task.Exception != null)
						throw task.Exception.InnerException;
					var ret = await task;
					j.PassReturn(Variable.FromExternalObject(ret, j.Heap));
				}
				else throw new NotImplementedException();
			}
			else
			{
				var task = (Task)tsk;
				if (task.Exception != null)
					throw task.Exception.InnerException;
				await task;
				j.ReturnNothing();
			}
		}
	}

	class ExternalWrapper : IReleaseMemory
	{
		public object Internal;
		public int GenerationID { get; set; }

		public void Release(Malloc memory)
		{
			GenerationID++;
			memory.CheckIn(this);
		}
	}

}
