using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ILanguage
{
	public class CodeProject
	{
		public string Path;
		Dictionary<string, ITextBuffer> _files = new Dictionary<string, ITextBuffer>();
		protected IServeCodeFiles _fileServer;
		event Action evAllFilesLoaded;
		public bool FullyLoaded = false;
		ITextBuffer _configFile;

		public CodeProject(string path, IServeCodeFiles fileServer)
		{
			Path = path;
			_fileServer = fileServer;
		}

		public async Task LoadConfigProject(string key)
		{
			_configFile = await LoadFile(key);
			LoadConfigProject(_configFile);
		}
		protected virtual void LoadConfigProject(ITextBuffer projectFile) { }

		// add a placeholder buffer to load later
		public virtual void AddLoadedFile(string key)
		{
			_files.Add(key, null);
		}

		public virtual async Task AllFilesAdded()
		{
			FullyLoaded = true;
			evAllFilesLoaded?.Invoke();
		}

		public virtual void DoHookWhenLoaded(Action act)
		{
			if (FullyLoaded) act();
			else evAllFilesLoaded += act;
		}

		public bool FileLoaded(string key)
		{
			return _files.ContainsKey(key) && _files[key] != null;
		}

		public virtual async Task<ITextBuffer> LoadFile(string key)
		{
			if (_files.ContainsKey(key) && _files[key] != null) return _files[key];
			var file = await _LoadByKey(key);
			if (_files.ContainsKey(key))
				_files[key] = file;
			else
				_files.Add(key, file);
			return file;
		}

		protected virtual async Task<ITextBuffer> _LoadByKey(string key)
		{
			var buff = new RawTextBuffer();
			await _fileServer.AsyncLoadFile(key, buff);
			buff.Dirty = false;
			return buff;
		}
		protected virtual async Task<ITextBuffer> _CreateNew(string key) { return new RawTextBuffer(key); }

		public async Task AddNewFile(string key)
		{
			ITextBuffer buff = await _CreateNew(key);
			_files.Add(key, buff);
		}

		public string GetKeyForBuffer(ITextBuffer buffer)
		{
			foreach (var pair in _files) // ewww
				if (pair.Value == buffer)
					return pair.Key;
			return "";
		}

		public bool IsFileDirty(string key)
		{
			if (!_files.ContainsKey(key)) return false;
			if (_files[key] == null) return false;
			return _files[key].Dirty;
		}

		public async Task SaveFile(ITextBuffer buffer)
		{
			var key = GetKeyForBuffer(buffer);
			if (key == "") throw new Exception("buffer does not have file path - save failed");
			await _fileServer.AsyncSaveFile(key, buffer);
			buffer.Dirty = false;
			if (buffer == _configFile)
				LoadConfigProject(_configFile);
		}

		public bool CanSaveFile(ITextBuffer buffer) => GetKeyForBuffer(buffer) != "";

		public virtual string FileExtension => "";
		public virtual string ProjExtension => "";

		public string GetFileName(string key) => _fileServer.GetFileDisplayName(key);
		public string GetFileName(ITextBuffer buffer)
		{
			var key = GetKeyForBuffer(buffer);
			if (key == "") return "";
			return GetFileName(key);
		}
		public string GetDirectoryName(string key) => _fileServer.GetFileDirectoryDisplay(key);
		public string GetDirectoryName(ITextBuffer buffer)
		{
			var key = GetKeyForBuffer(buffer);
			if (key == "") return "";
			return GetDirectoryName(key);
		}
	}

	public interface IServeCodeFiles
	{
		Task AsyncLoadFile(string key, ITextBuffer buffer);
		Task AsyncSaveFile(string key, ITextBuffer buffer);

		string GetFileDisplayName(string key);
		string GetFileDirectoryDisplay(string key);
	}
}
