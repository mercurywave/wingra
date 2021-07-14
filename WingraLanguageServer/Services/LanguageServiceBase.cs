using JsonRpc.Server;
using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Contracts.Client;
using LanguageServer.VsCode.Server;
using System;
using System.Collections.Generic;
using System.Text;

namespace WingraLanguageServer.Services
{
	public class LanguageServiceBase : JsonRpcService
	{
		protected LanguageServerSession Session => RequestContext.Features.Get<LanguageServerSession>();

		protected ClientProxy Client => Session.Client;

		protected TextDocument GetDocument(Uri uri)
		{
			if (Session.Documents.TryGetValue(uri, out var sd))
				return sd.Document;
			return null;
		}

		protected TextDocument GetDocument(TextDocumentIdentifier id) => GetDocument(id.Uri);

		protected void Debug(string text)
		{
			//Session.Client.Window.LogMessage(MessageType.Info, text);
			Session.Client.Window.ShowMessage(MessageType.Info, text);
		}
	}
}
