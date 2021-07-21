using JsonRpc.Contracts;
using JsonRpc.Server;
using LanguageServer.VsCode.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace WingraLanguageServer.Services
{
	[JsonRpcScope(MethodPrefix = "completionItem/")]
	public class CompletionItemService : LanguageServiceBase
	{
		// The request is sent from the client to the server to resolve additional information
		// for a given completion item.
		[JsonRpcMethod(AllowExtensionData = true)]
		public CompletionItem Resolve()
		{
			var item = RequestContext.Request.Parameters.ToObject<CompletionItem>(util.CamelCaseJsonSerializer);
			// Add a pair of square brackets around the inserted text.
			item.InsertText = item.Label; //$"[{item.Label}]";
			item.CommitCharacters = new List<char>() { ' ', '(', ';' };
			return item;
		}
	}
}
