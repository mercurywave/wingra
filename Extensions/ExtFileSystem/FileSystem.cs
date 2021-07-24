using System;
using System.IO;
using System.Threading.Tasks;
using Wingra.Interpreter;

namespace ExtFileSystem
{
	[WingraLibrary("File")]
	static class FileSystem
	{
		public static async Task<string> ReadText(string path)
		{
			try
			{
				return await File.ReadAllTextAsync(path);
			}
			catch (Exception e)
			{
				throw new CatchableError(new Variable(e.Message));
			}
		}
	}
}
