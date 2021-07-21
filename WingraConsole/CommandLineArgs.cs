using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WingraConsole
{
	class CommandLineArgs
	{
		public bool Verbose = false;

		public static CommandLineArgs Parse(string[] args)
		{
			var obj = new CommandLineArgs();
			if (args == null) return obj;
			int i = 0;
			while(TryNext(args, ref i, out var token))
			{
				if (OneOf(token, "-v", "-verbose", "--v"))
					obj.Verbose = true;
				else throw new Exception("Unknown command line parameter '" + token + "'");
			}
			return obj;
		}
		static bool OneOf(string key, params string[] matches)
			=> matches.Any(m => m.ToLower() == key.ToLower());

		static bool TryNext(string[] args, ref int i, out string tok)
			=> TryGet(args, ++i, out tok);
		
		static bool TryPeek(string[] args, int i, out string tok)
			=> TryGet(args, i + 1, out tok);
		static bool TryGet(string[] args, int i, out string tok)
		{
			tok = null;
			if (args == null) return false;
			if (i >= args.Length) return false;
			tok = args[i];
			return true;
		}
	}
}
