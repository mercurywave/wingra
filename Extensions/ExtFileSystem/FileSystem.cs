using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wingra.Interpreter;

namespace ExtFileSystem
{
	[WingraLibrarySetup]
	class FileSystem
	{
		ORuntime _run;
		public static void WingraInit(ORuntime run)
		{
			var fs = new FileSystem() { _run = run };
			run.InjectDynamicLibrary(fs, "File");
		}
		public async Task<string> ReadText(string path)
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
		public async Task<Variable> ReadAllLines(string path)
		{
			try
			{
				var str = await File.ReadAllTextAsync(path);
				var split = str.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
				return _run.MakeList(split);
			}
			catch (Exception e)
			{
				throw new CatchableError(new Variable(e.Message));
			}
		}

		[WingraMethod]
		public bool Exists(string filePath)
			=> (new FileInfo(filePath)).Exists;
	}

	[WingraLibrary("Path")]
	public static class PathSystem
	{
		[WingraMethod]
		public static string GetDirName(string dir)
			=> Path.GetDirectoryName(dir) ?? "";

		[WingraMethod]
		public static string GetFileName(string dir)
			=> Path.GetFileName(dir) ?? "";

		[WingraMethod]
		public static string GetExtension(string dir)
			=> Path.GetExtension(dir) ?? "";

		[WingraMethod]
		public static string GetAbsolute(string dir)
			=> Path.GetFullPath(dir) ?? "";

		public static string Join(string basePath, string relPath)
			=> Path.Combine(basePath, relPath);
	}


	[WingraLibrarySetup]
	class DirSystem
	{
		ORuntime _run;
		public static void WingraInit(ORuntime run)
		{
			var fs = new DirSystem() { _run = run };
			run.InjectDynamicLibrary(fs, "Dir");
		}

		[WingraMethod]
		public Variable GetDirectories(string dir)
		{
			try
			{
				var info = new DirectoryInfo(dir);
				var list = info.GetDirectories().Select(d => d.Name);
				return _run.MakeList(list.ToList());
			}
			catch (Exception e)
			{
				throw new CatchableError(new Variable(e.Message));
			}
		}

		[WingraMethod]
		public Variable GetFiles(string dir)
		{
			try
			{
				var info = new DirectoryInfo(dir);
				var list = info.GetFiles().Select(f => f.Name);
				return _run.MakeList(list.ToList());
			}
			catch (Exception e)
			{
				throw new CatchableError(new Variable(e.Message));
			}
		}

		public string WorkingDirectory()
			=> Directory.GetCurrentDirectory();
	}
}
