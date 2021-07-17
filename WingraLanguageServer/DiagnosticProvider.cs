using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Server;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Range = LanguageServer.VsCode.Contracts.Range;

namespace WingraLanguageServer
{
	public class DiagnosticProvider
	{

		public DiagnosticProvider()
		{

		}

		private static readonly string[] Keywords =
			{".NET Framework", ".NET Core", ".NET Standard", ".NET Compact", ".NET"};

		public ICollection<Diagnostic> LintDocument(LanguageServerSession Session, string key)
		{
			var diag = new List<Diagnostic>();

			lock (Session.Lock)
			{
				if (Session.IsLoaded && Session.Prj.IsFileLoaded(key))
				{
					var buffer = Session.Prj.GetFile(key);
					foreach (var err in Session.Prj.IncrementalErrorList.Errors)
					{
						if (err.Buffer == buffer)
						{
							var line = err.Line;
							if (err.Token.HasValue)
							{
								var off = err.Token.Value.Token.LineOffset;
								diag.Add(new Diagnostic(DiagnosticSeverity.Error,
									new Range(err.Line, off, err.Line, off + err.Token.Value.Token.Length),
									buffer.Key,
									"",
									err.Text + "\n" + err.ExtraText));
							}
							else diag.Add(new Diagnostic(DiagnosticSeverity.Error,
								new Range(err.Line, 0, err.Line, 0),
								buffer.Key,
								"",
								err.Text + "\n" + err.ExtraText));
						}
					}
				}
			}

			return diag;
		}
	}
}
