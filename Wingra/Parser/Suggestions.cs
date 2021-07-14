using Wingra.Interpreter;
using Wingra.Parser;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Wingra
{
	public enum eSuggestionType { Method, Property, Library, LibraryFunc, LibraryProp, Function, Local, Keyword, Template, Record }
	public struct Suggestion
	{
		public eSuggestionType Type;
		public string Library;
		public string Function;
		public string[] Params;
		public Suggestion(eSuggestionType type, string lib)
		{
			Type = type;
			Library = lib;
			Function = "";
			Params = null;
		}
		public Suggestion(eSuggestionType type, string lib, string func, params string[] pars)
		{
			Type = type;
			Library = lib;
			Function = func;
			Params = pars;
		}

		// specifically built in methods and types - keywords and external types make a little less sense within this project
		public static List<Suggestion> GetBuiltIns(StaticMapping mapping)
		{
			List<Suggestion> list = new List<Suggestion>();
			//Scan(list);
			ORuntime temp = new ORuntime();
			// the number of special cases here is fairly small for the moment, not worth fancy system
			LCompiler.Setup(temp, new Compiler(mapping, true, true, true, true));

			foreach (var pair in LexLine._tokenMap)
				if (BaseToken.IsTokenReserved(pair.Value))
					list.Add(new Suggestion(eSuggestionType.Keyword, "", pair.Key));

			return list;
		}

		internal static void Scan(List<Suggestion> list)
		{
			Scan(typeof(Suggestion).GetTypeInfo().Assembly, list);
		}
		internal static void Scan(Assembly assembly, List<Suggestion> list)
		{
			var types = assembly.GetTypes();
			foreach (var t in types)
				ScanType(t, list);
		}

		internal static void ScanType(Type t, List<Suggestion> list)
		{
			var name = t.Name;
			var classAttr = t.GetTypeInfo().GetCustomAttribute<IHaveBuiltIns>();

			if (!(classAttr is IHaveBuiltIns)) return;

			var init = t.GetMethod("BuiltInInit", BindingFlags.Static | BindingFlags.NonPublic);
			if (init == null) throw new NotImplementedException("couldn't find static method BuiltInInit()");
			init.Invoke(null, new object[] { list });
		}

		public static Suggestion Method(string obj, string func, params string[] pars) => new Suggestion(eSuggestionType.Method, obj, func, pars);
		public static Suggestion Property(string obj, string property) => new Suggestion(eSuggestionType.Method, obj, property);
		public static Suggestion LibraryFunc(string lib, string func, params string[] pars) => new Suggestion(eSuggestionType.LibraryFunc, lib, func, pars);
		public static Suggestion LibraryProp(string lib, string func) => new Suggestion(eSuggestionType.LibraryProp, lib, func);
		public static Suggestion GenLibrary(string lib) => new Suggestion(eSuggestionType.Library, lib);
		public static Suggestion Template(string template) => new Suggestion(eSuggestionType.Template, template);
		public static Suggestion Record(string template, string reg) => new Suggestion(eSuggestionType.Record, template, reg);
	}

	// assumes you have a static method called BuiltInInit that returns a list of suggestions
	[AttributeUsage(AttributeTargets.Class)]
	internal class IHaveBuiltIns : Attribute
	{
	}
}
