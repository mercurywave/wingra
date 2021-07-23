using ILanguage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Wingra;

namespace WingraLanguageServer
{
	static class Loader
	{
		internal static string AsmPath => Path.GetDirectoryName(Assembly.GetAssembly(typeof(Loader)).Location);
		internal static string StdLibPath => Path.Combine(AsmPath, "..", "lang", "StdLib");
		internal static string ExtLibPath => Path.Combine(AsmPath, "..", "lang", "extensions");
		public static async Task<WingraProject> LoadProject(string path, DocFileServer server)
		{
			var cache = new Dictionary<string, WingraProject>();
			var stdLib = await LoadProject(StdLibPath, cache, server);
			var prj = await LoadProject(path, cache, server, stdLib);
			return prj;
		}

		static async Task<WingraProject> LoadProject(string path, Dictionary<string, WingraProject> cache, DocFileServer server, WingraProject stdLib = null)
		{
			if (cache.ContainsKey(path))
				return cache[path];

			var prj = new WingraProject(path, server, stdLib);
			cache.Add(path, prj);
			fileUtils.PreLoadDirectory(path, prj);
			await LoadDependentProjects(prj, path, cache, server);
			await prj.AllFilesAdded();
			return prj;
		}

		static async Task LoadDependentProjects(WingraProject prj, string dir, Dictionary<string, WingraProject> cache, DocFileServer server, WingraProject stdLib = null)
		{
			var file = fileUtils.CombinePath(dir, "project." + prj.ProjExtension);
			if (!fileUtils.FileExists(file))
				return;
			await prj.LoadConfigProject(file);
			foreach (var key in prj.Extensions)
			{
				var exePath = Path.GetDirectoryName(Assembly.GetAssembly(typeof(Loader)).Location);
				var absPath = Path.GetFullPath(Path.Combine(exePath, "..", "lang", "extensions", key));
				var child = await LoadProject(absPath, cache, server, stdLib);
				prj.RequiredProjects.Add(child);
			}
			foreach (var path in prj.RequiredPaths)
			{
				var absPath = Path.GetFullPath(Path.Combine(dir, path));
				var child = await LoadProject(absPath, cache, server, stdLib);
				prj.RequiredProjects.Add(child);
			}
		}
	}
	static class fileUtils
	{
		// TODO: HACK: DirectoryInfo can't seem to understand the file: prefix, and I can't figure out the proper way to convert
		// TODO: HACK: there is a right way to get file paths - need to make sure file keys match
		public static string CleanPath(string uri) => uri.Replace("file:///", "").Replace("/", "\\");
		public static string UriTRoPath(Uri uri) => CleanPath(Uri.UnescapeDataString(uri.AbsoluteUri));
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

		public static string GetFileExtension(string path)
			=> new FileInfo(path).Extension;

		public static string RelativePath(string fromPath, string toTarget)
			=> Path.GetRelativePath(fromPath, toTarget);

		public static bool IsFileInPath(string file, string folder)
		{
			var rel = Path.GetRelativePath(folder, file);
			return !(rel.StartsWith(".") || Path.IsPathRooted(rel));
		}

		public static Uri FileToUri(string file)
			=> new Uri("file:///" + Uri.EscapeUriString(file.Replace(Path.DirectorySeparatorChar, '/')), UriKind.Absolute);
	}

	class DocFileServer : IServeCodeFiles
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

		public Task AsyncSaveFile(string key, ITextBuffer buffer)
		{
			throw new NotImplementedException();
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
