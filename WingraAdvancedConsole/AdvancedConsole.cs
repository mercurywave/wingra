using System;
using System.Collections.Generic;
using System.Text;
using Wingra;

namespace WingraAdvancedConsole
{
	[Wingra.Interpreter.WingraLibrary("Console")]
	static class AdvancedConsole
	{
		public static void Clear() => Console.Clear();
		public static void Write(string str) => Console.Write(str);
		public static void SetBufferSize(int w, int h) => Console.SetBufferSize(w, h);
		public static void SetCursorPosition(int x, int y) => Console.SetCursorPosition(x, y);
	}
}
