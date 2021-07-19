using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using JsonRpc.Contracts;
using LanguageServer.VsCode.Contracts;
using Newtonsoft.Json.Linq;
using Wingra;
using Wingra.Parser;

namespace WingraLanguageServer.Services
{
	[JsonRpcScope(MethodPrefix = "workspace/")]
	public class WorkspaceService : LanguageServiceBase
	{
		[JsonRpcMethod(IsNotification = true)]
		public async Task DidChangeConfiguration(SettingsRoot settings)
		{
			Session.Settings = settings.LanguageServer;
			foreach (var doc in Session.Documents.Values)
			{
				var diag = Session.DiagnosticProvider.LintDocument(Session, fileUtils.UriTRoPath(doc.Document.Uri));
				await Client.Document.PublishDiagnostics(doc.Document.Uri, diag);
			}
		}

		[JsonRpcMethod(IsNotification = true)]
		public async Task DidChangeWatchedFiles(ICollection<FileEvent> changes)
		{
			foreach (var change in changes)
			{
				if (!change.Uri.IsFile) continue;
				var localPath = change.Uri.AbsolutePath;
				if (string.Equals(Path.GetExtension(localPath), ".wng"))
				{
					// If the file has been removed, we will clear the lint result about it.
					// Note that pass null to PublishDiagnostics may mess up the client.
					if (change.Type == FileChangeType.Deleted)
					{
						await Client.Document.PublishDiagnostics(change.Uri, new Diagnostic[0]);
					}
				}
			}
		}
		[JsonRpcMethod]
		public async Task ExecuteCommand(string command, ICollection<string> arguments)
		{
			try
			{
				//if (command == "wingra.build")
				//{
				//	lock (Session.Lock)
				//	{
				//		var prj = Session.Prj;
				//		var symbols = new WingraSymbols();
				//		var compiler = new Compiler(Session._staticMap, false, true, false, true, false);
				//		prj.CompileAll(compiler, symbols);

				//		Debug("errors:" + prj.GetAllErrors().Count);
				//	}
				//	foreach (var doc in Session.Documents.Values)
				//	{
				//		var diag = Session.DiagnosticProvider.LintDocument(Session, fileUtils.UriTRoPath(doc.Document.Uri));
				//		await Client.Document.PublishDiagnostics(doc.Document.Uri, diag);
				//	}
				//}
			}
			catch (Exception e) { Debug(e.ToString()); }
		}
	}
}
