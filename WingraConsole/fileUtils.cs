using ILanguage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Wingra;

namespace WingraConsole
{
	static class Loader
	{
		public static async Task<WingraProject> LoadProject(string path)
		{
			var cache = new Dictionary<string, WingraProject>();
			// StdLib always loads first
			var libDir = Path.GetDirectoryName(Assembly.GetAssembly(typeof(Loader)).Location);
			libDir = fileUtils.CombinePath(libDir, ".StdLib");
			var stdLib = await LoadProject(libDir, cache);
			var prj = await LoadProject(path, cache, stdLib);
			return prj;
		}

		static async Task<WingraProject> LoadProject(string path, Dictionary<string, WingraProject> cache, WingraProject stdLib = null)
		{
			if (cache.ContainsKey(path))
				return cache[path];

			var prj = new WingraProject(path, new CodeFileServer(), stdLib);
			cache.Add(path, prj);
			fileUtils.PreLoadDirectory(path, prj);
			await LoadDependentProjects(prj, path, cache);
			await prj.AllFilesAdded();
			return prj;
		}

		static async Task LoadDependentProjects(WingraProject prj, string dir, Dictionary<string, WingraProject> cache, WingraProject stdLib = null)
		{
			var file = fileUtils.CombinePath(dir, "project." + prj.ProjExtension);
			if (!fileUtils.FileExists(file))
				return;
			await prj.LoadConfigProject(file);
			foreach (var key in prj.Extensions)
			{
				var exePath = Path.GetDirectoryName(Assembly.GetAssembly(typeof(Loader)).Location);
				var absPath = Path.GetFullPath(Path.Combine(exePath, "extensions", key));
				var child = await LoadProject(absPath, cache, stdLib);
				prj.RequiredProjects.Add(child);
			}
			foreach (var path in prj.RequiredPaths)
			{
				var absPath = Path.GetFullPath(Path.Combine(dir, path));
				var child = await LoadProject(absPath, cache, stdLib);
				prj.RequiredProjects.Add(child);
			}
		}
	}
	class fileUtils
	{
		public static async Task LoadFileAsync(string filename, ITextBuffer buffer)
		{
			using (var stream = File.OpenText(filename))
			{
				while (!stream.EndOfStream)
				{
					string line = await stream.ReadLineAsync();
					buffer.AppendLine(line);
				}
				stream.Close();
			}
		}

		public static async Task LoadDirectory(string path, WingraProject proj)
		{
			PreLoadDirectory(path, proj);
			await LoadDirectory(proj);
		}

		public static void PreLoadDirectory(string path, WingraProject proj)
		{
			DirectoryInfo topLevel = new DirectoryInfo(path);
			AddFolderToTree(topLevel, proj);
		}

		// Expects you called PreLoad first!
		public static async Task LoadDirectory(WingraProject proj)
		{
			await proj.AllFilesAdded();
		}

		static void AddFolderToTree(DirectoryInfo dir, WingraProject proj)
		{
			foreach (DirectoryInfo child in dir.GetDirectories())
				if (!child.Name.StartsWith('.'))
					AddFolderToTree(child, proj);
			foreach (FileInfo file in dir.GetFiles())
			{
				FileSystemInfo info = file;
				if (file.Extension.ToLower() == "." + proj.FileExtension)
					proj.AddLoadedFile(file.FullName);
			}
		}

		public static bool DirectoryExists(string path)
			=> new DirectoryInfo(path).Exists;

		public static string CombinePath(string dir, string file)
			=> Path.Combine(dir, file);

		public static bool FileExists(string path)
			=> new FileInfo(path).Exists;
	}
	class CodeFileServer : IServeCodeFiles
	{
		public async Task AsyncLoadFile(string filename, ITextBuffer buffer)
		{
			try
			{
				using (var stream = File.OpenText(filename))
				{
					while (!stream.EndOfStream)
					{
						string line = await stream.ReadLineAsync();
						buffer.AppendLine(line);
					}
					stream.Close();
				}
			}
			catch (Exception e) { throw new Exception("Error reading file " + filename + "\n" + e.ToString()); }
		}

		public Task AsyncSaveFile(string filename, ITextBuffer buffer)
		{
			throw new NotImplementedException();
		}

		public static async Task AsyncSaveFile(string filename, StringBuilder sb)
		{
			try
			{
				var fi = new FileInfo(filename);
				if (!fi.Exists)
				{
					var temp = File.CreateText(filename);
					temp.Close(); // la-zy
				}
				using (var stream = File.Open(filename, FileMode.Truncate, FileAccess.Write))
				using (var writer = new StreamWriter(stream))
					await writer.WriteAsync(sb.ToString());
			}
			catch (Exception e) { throw new Exception("Error writing file " + filename + "\n" + e.ToString()); }
		}

		public static string FlattenRelativePath(string targetFolder, string key)
		{
			var dir = Directory.CreateDirectory(targetFolder);
			FileInfo source = new FileInfo(key);
			var rel = Path.GetRelativePath(Environment.CurrentDirectory, source.FullName);
			var clean = rel.Replace("/", "_").Replace("\\", "_");
			clean = Path.ChangeExtension(clean, ".wobj");
			return Path.Combine(targetFolder, clean);
		}

		public string GetFileDirectoryDisplay(string key)
		{
			FileInfo file = new FileInfo(key);
			return file.Directory.FullName;
		}

		public string GetFileDisplayName(string key)
		{
			FileInfo file = new FileInfo(key);
			return file.Name;
		}
	}
}
