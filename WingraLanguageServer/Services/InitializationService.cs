using JsonRpc.Contracts;
using JsonRpc.Messages;
using JsonRpc.Server;
using LanguageServer.VsCode.Contracts;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wingra;
using Wingra.Parser;

namespace WingraLanguageServer.Services
{
	public class InitializationService : LanguageServiceBase
	{
		[JsonRpcMethod(AllowExtensionData = true)]
		public InitializeResult Initialize(int processId, Uri rootUri, ClientCapabilities capabilities,
			JToken initializationOptions = null, string trace = null)
		{
			Session._folderPath = fileUtils.UriTRoPath(rootUri);
			return new InitializeResult(new ServerCapabilities
			{
				HoverProvider = new HoverOptions(),
				SignatureHelpProvider = new SignatureHelpOptions("()"),
				CompletionProvider = new CompletionOptions(true, "."),
				TextDocumentSync = new TextDocumentSyncOptions
				{
					OpenClose = true,
					WillSave = true,
					Change = TextDocumentSyncKind.Incremental
				},
				DefinitionProvider = new DefinitionOptions(),
				//ExecuteCommandProvider = new ExecuteCommandOptions(new List<string> { 
				//	"wingra.build"
				//}),
			});
		}

		[JsonRpcMethod(IsNotification = true)]
		public async Task Initialized()
		{
			try
			{
				await Session.InitializeAsync();
				//await Client.Window.ShowMessage(MessageType.Info, $"Wingra parser booted");
			}
			catch(Exception e)
			{
				await Client.Window.ShowMessage(MessageType.Error, $"Could not load language service: {e.ToString()}");
			}
		}

		[JsonRpcMethod]
		public void Shutdown()
		{

		}

		[JsonRpcMethod(IsNotification = true)]
		public void Exit()
		{
			Session.StopServer();
		}

		[JsonRpcMethod("$/cancelRequest", IsNotification = true)]
		public void CancelRequest(MessageId id)
		{
			RequestContext.Features.Get<IRequestCancellationFeature>().TryCancel(id);
		}
	}
}
