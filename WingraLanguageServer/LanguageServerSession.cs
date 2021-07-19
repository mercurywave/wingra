using ILanguage;
using JsonRpc.Client;
using JsonRpc.Contracts;
using JsonRpc.DynamicProxy.Client;
using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Contracts.Client;
using LanguageServer.VsCode.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wingra;
using Wingra.Parser;

namespace WingraLanguageServer
{
	public class LanguageServerSession
	{
		private readonly CancellationTokenSource cts = new CancellationTokenSource();

		internal WingraProject Prj = null;
		public object Lock = new object();
		internal Compiler Cmplr => Prj.IncrementalDebugCompiler;
		internal DocFileServer FileServer = new DocFileServer();
		internal string _folderPath;
		internal Dictionary<WingraBuffer, STopOfFile> _parsedFiles = new Dictionary<WingraBuffer, STopOfFile>();
		internal Dictionary<WingraBuffer, FileScopeTracker> _scopeTracker = new Dictionary<WingraBuffer, FileScopeTracker>();
		internal StaticMapping _staticMap = new StaticMapping();

		internal List<CompletionItem> StaticSuggestions = new List<CompletionItem>();

		Task _loadTask = null;
		internal Task LoadTask => _loadTask ?? Task.CompletedTask;
		internal bool IsLoaded => _loadTask?.IsCompleted ?? false;
		public LanguageServerSession(JsonRpcClient rpcClient, IJsonRpcContractResolver contractResolver)
		{
			RpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
			var builder = new JsonRpcProxyBuilder { ContractResolver = contractResolver };
			Client = new ClientProxy(builder, rpcClient);
			Documents = new ConcurrentDictionary<Uri, SessionDocument>();
			DiagnosticProvider = new DiagnosticProvider();
		}

		public CancellationToken CancellationToken => cts.Token;

		public JsonRpcClient RpcClient { get; }

		public ClientProxy Client { get; }

		public ConcurrentDictionary<Uri, SessionDocument> Documents { get; }

		public DiagnosticProvider DiagnosticProvider { get; }

		public LanguageServerSettings Settings { get; set; } = new LanguageServerSettings();

		public void StopServer()
		{
			cts.Cancel();
		}

		internal async Task InitializeAsync()
		{
			_loadTask = _InitializeAsync();
			await _loadTask;
		}
		internal async Task _InitializeAsync()
		{
			Prj = await Loader.LoadProject(_folderPath, FileServer);
			Prj.IncrementalDebugCompiler = new Compiler(_staticMap, false, false, true, true);
			foreach (var sug in Suggestion.GetBuiltIns(_staticMap))
			{
				if (sug.Type == eSuggestionType.Keyword)
					StaticSuggestions.Add(new CompletionItem(sug.Function, CompletionItemKind.Keyword, null));
			}
			BuildCache();
		}
		void BuildCache()
		{
			MinimalErrorLogger log = new MinimalErrorLogger();
			// pre-parse to get all macros up front before attempting to fully compile anything
			foreach (var file in Prj.IterAllFilesRecursive().ToList())
				Cmplr.PreParse(file, log);
			foreach (var file in Prj.IterAllFilesRecursive().ToList())
				UpdateFileCache(file);
		}
		internal void UpdateFileCache(WingraBuffer file)
		{
			Prj.ClearFileErrors(file);
			var log = Prj.GetFileErrorLogger(file);
			try
			{
				// TODO: does this comment still apply?
				// macro support is definitely buggy here, but it works in the common cases
				// macros are never cleaned, so old macros continue to apply
				// probably not a common problem
				// only really matters if you delete one but still use it?
				_staticMap.FlushFile(file.Key); // clear cruft
				Cmplr.ClearMacroCacheForFile(file.Key);
				Cmplr.PreParse(file, log);
				Cmplr.Bootstrap(log);
				var tracker = new FileScopeTracker();
				_parsedFiles[file] = Cmplr.Parse(file, log, tracker);
				_scopeTracker[file] = tracker;
			}
			catch (Exception e)
			{
				// do nothing?
			}
		}
	}

	public class SessionDocument
	{
		/// <summary>
		/// Actually makes the changes to the inner document per this milliseconds.
		/// </summary>
		//private const int RenderChangesDelay = 100;

		public SessionDocument(TextDocumentItem doc)
		{
			Document = TextDocument.Load<FullTextDocument>(doc);
		}

		//private Task updateChangesDelayTask;

		private readonly object syncLock = new object();

		private List<TextDocumentContentChangeEvent> impendingChanges = new List<TextDocumentContentChangeEvent>();

		public event EventHandler DocumentChanged;

		public TextDocument Document { get; set; }

		public void NotifyChanges(IEnumerable<TextDocumentContentChangeEvent> changes)
		{
			lock (syncLock)
			{
				if (impendingChanges == null)
					impendingChanges = changes.ToList();
				else
					impendingChanges.AddRange(changes);
			}
			//if (updateChangesDelayTask == null || updateChangesDelayTask.IsCompleted)
			//{
			//	updateChangesDelayTask = Task.Delay(RenderChangesDelay);
			//	updateChangesDelayTask.ContinueWith(t => Task.Run((Action)MakeChanges), TaskScheduler.Current);
			//}
			MakeChanges();
		}

		private void MakeChanges()
		{
			List<TextDocumentContentChangeEvent> localChanges;
			lock (syncLock)
			{
				localChanges = impendingChanges;
				if (localChanges == null || localChanges.Count == 0) return;
				impendingChanges = null;
			}
			Document = Document.ApplyChanges(localChanges);
			if (impendingChanges == null)
			{
				localChanges.Clear();
				lock (syncLock)
				{
					if (impendingChanges == null)
						impendingChanges = localChanges;
				}
			}
			OnDocumentChanged();
		}

		protected virtual void OnDocumentChanged()
		{
			DocumentChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
