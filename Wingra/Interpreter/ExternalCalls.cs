using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

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

		enum eConvert { none, str, integer, boolean, single }
		static private void ParseReflectionHelper(ORuntime runtime, string path, IEnumerable<MethodInfo> calls, object host)
		{
			foreach (var meth in calls)
			{
				ParseMethod(meth, out var _retConv, out var _convArr);

				var lamb = new ExternalFuncPointer((j, t) =>
				{
					RunParseMethod(j, host, meth, _retConv, _convArr);
				});
				runtime.InjectStaticVar(MakeFuncPath(path, meth.Name), new Variable(lamb), Parser.eStaticType.External, "", -1);
			}
		}

		private static void ParseMethod(MethodInfo meth, out eConvert? _retConv, out eConvert[] _convArr)
		{
			List<eConvert> _convList = new List<eConvert>();
			_retConv = null;
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
				else
					_convList.Add(eConvert.none);
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
			}
		}

		private static void RunParseMethod(Job j, object host, MethodInfo meth, eConvert? _retConv, eConvert[] _convArr)
		{
			List<object> inputs = new List<object>(j.TotalParamsPassing);
			for (int i = 0; i < j.TotalParamsPassing; i++)
			{
				var pass = j.GetPassingParam(i);
				var conv = _convArr[i];
				object obj;
				if (conv == eConvert.integer)
					obj = pass.AsInt();
				else if (conv == eConvert.boolean)
					obj = pass.AsBool();
				else if (conv == eConvert.single)
					obj = pass.AsFloat();
				else if (conv == eConvert.str)
					obj = pass.AsString();
				else obj = pass;
				inputs.Add(obj);
			}
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
				else throw new NotImplementedException();
			}
			else j.ReturnNothing();
		}
	}


}
