using JsonRpc.Client;
using JsonRpc.Contracts;
using JsonRpc.Server;
using JsonRpc.Streams;
using LanguageServer.VsCode;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.IO;
using System.Reflection;

namespace WingraLanguageServer
{
	static class Program
	{
		static void Main(string[] args)
		{
			using (var cin = Console.OpenStandardInput())
			using (var bcin = new BufferedStream(cin))
			using (var cout = Console.OpenStandardOutput())
			using (var reader = new  PartwiseStreamMessageReader(bcin))
			using (var writer = new PartwiseStreamMessageWriter(cout))
			{

				var contractResolver = new JsonRpcContractResolver
				{
					NamingStrategy = new CamelCaseJsonRpcNamingStrategy(),
				};
				var clientHandler = new StreamRpcClientHandler();
				var client = new JsonRpcClient(clientHandler);

				// Configure & build service host
				var session = new LanguageServerSession(client, contractResolver);
				var host = BuildServiceHost(contractResolver);
				var serverHandler = new  StreamRpcServerHandler(host,
					StreamRpcServerHandlerOptions.ConsistentResponseSequence |
					StreamRpcServerHandlerOptions.SupportsRequestCancellation);
				serverHandler.DefaultFeatures.Set(session);
				// If we want server to stop, just stop the "source"
				using (serverHandler.Attach(reader, writer))
				using (clientHandler.Attach(reader, writer))
				{
					// Wait for the "stop" request.
					session.CancellationToken.WaitHandle.WaitOne();
				}
			}
		}
		private static IJsonRpcServiceHost BuildServiceHost(IJsonRpcContractResolver contractResolver)
		{
			var builder = new JsonRpcServiceHostBuilder
			{
				ContractResolver = contractResolver
			};
			builder.UseCancellationHandling();
			builder.Register(typeof(Program).GetTypeInfo().Assembly);
			return builder.Build();
		}

	}

	internal static class Utility
	{
		public static readonly JsonSerializer CamelCaseJsonSerializer = new JsonSerializer
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver()
		};

		public static string GetTimeStamp()
		{
			return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		}
	}
}
