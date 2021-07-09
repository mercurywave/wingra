using ILanguage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wingra;

namespace WingraConsole
{
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
			await proj.LoadAllFiles();
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
