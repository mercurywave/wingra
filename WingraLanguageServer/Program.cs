// #define WAIT_FOR_DEBUGGER

using JsonRpc.Client;
using JsonRpc.Contracts;
using JsonRpc.Server;
using JsonRpc.Streams;
using LanguageServer.VsCode;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Wingra;

namespace WingraLanguageServer
{
	static class Program
	{
		static void Main(string[] args)
		{
			var debugMode = args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase));
#if WAIT_FOR_DEBUGGER
            while (!Debugger.IsAttached) Thread.Sleep(1000);
            Debugger.Break();
#endif
			StreamWriter logWriter = null;
			if (debugMode)
			{
				logWriter = File.CreateText("messages-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".log");
				logWriter.AutoFlush = true;
				var debugLog  = File.CreateText("debug-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".log");
				debugLog.AutoFlush = true;
				util._logger = debugLog;
			}
			using (logWriter)
			using (var cin = Console.OpenStandardInput())
			using (var bcin = new BufferedStream(cin))
			using (var cout = Console.OpenStandardOutput())
			using (var reader = new  PartwiseStreamMessageReader(bcin))
			using (var writer = new PartwiseStreamMessageWriter(cout))
			{

				var contractResolver = new JsonRpcContractResolver
				{
					NamingStrategy = new CamelCaseJsonRpcNamingStrategy(),
					ParameterValueConverter = new CamelCaseJsonValueConverter(),
				};
				var clientHandler = new StreamRpcClientHandler();
				var client = new JsonRpcClient(clientHandler);
				if (debugMode)
				{
					// We want to capture log all the LSP server-to-client calls as well
					clientHandler.MessageSending += (_, e) =>
					{
						lock (logWriter) logWriter.WriteLine("{0} <C{1}", util.GetTimeStamp(), e.Message);
					};
					clientHandler.MessageReceiving += (_, e) =>
					{
						lock (logWriter) logWriter.WriteLine("{0} >C{1}", util.GetTimeStamp(), e.Message);
					};
				}

				// Configure & build service host
				var session = new LanguageServerSession(client, contractResolver);
				var host = BuildServiceHost(logWriter, contractResolver, debugMode);
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
				logWriter?.WriteLine("Exited");
			}
		}
		private static IJsonRpcServiceHost BuildServiceHost(TextWriter logWriter,
			IJsonRpcContractResolver contractResolver, bool debugMode)
		{
			var loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
			if (debugMode)
			{
				loggerFactory.AddFile("logs-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".log");
			}
			var builder = new JsonRpcServiceHostBuilder
			{
				ContractResolver = contractResolver,
				LoggerFactory = loggerFactory
			};
			builder.UseCancellationHandling();
			builder.Register(typeof(Program).GetTypeInfo().Assembly);
			if (debugMode)
			{
				// Log all the client-to-server calls.
				builder.Intercept(async (context, next) =>
				{
					lock (logWriter) logWriter.WriteLine("{0} > {1}", util.GetTimeStamp(), context.Request);
					await next();
					lock (logWriter) logWriter.WriteLine("{0} < {1}", util.GetTimeStamp(), context.Response);
				});
			}
			return builder.Build();
		}

	}

	internal static class util
	{
		public static readonly JsonSerializer CamelCaseJsonSerializer = new JsonSerializer
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver()
		};
		public static string Join(IEnumerable<string> pieces, string delim)
		{
			return string.Join(delim, pieces);
		}
		public static string GetTimeStamp()
		{
			return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		}

		internal static TextWriter _logger;
		public static void Log(object obj)
		{
			if(_logger != null)
			lock (_logger) _logger.WriteLine("{0} < {1}", GetTimeStamp(), obj);
		}
		public static string AppendPiece(string original, string delim, string append)
		{
			if (string.IsNullOrEmpty(original)) return append;
			return original + delim + append;
		}
	}
}
