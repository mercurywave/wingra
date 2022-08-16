using LanguageServer.VsCode.Contracts;
using LanguageServer.VsCode.Server;
using System;
using System.Collections.Generic;
using System.Linq;
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

		public ICollection<Diagnostic> LintDocument(LanguageServerSession Session, string key)
		{
			var diag = new List<Diagnostic>();

			lock (Session.Lock)
			{
				if (Session.IsLoaded && Session.Prj.IsFileLoaded(key))
				{
					var buffer = Session.Prj.GetFile(key);
					foreach (var err in Session.Prj.GetAllErrors())
					{
						if (err.Buffer == buffer)
						{
							var line = err.Line;
							if (line < 0 || line > buffer.Lines) line = 0;
							var code = "";
							var sever = err.Type == Wingra.Parser.eErrorType.Warning ?
								DiagnosticSeverity.Warning : DiagnosticSeverity.Error;
							if (err.Token.HasValue)
							{
								var off = err.Token.Value.Token.LineOffset;
								diag.Add(new Diagnostic(sever,
									new Range(line, off, line, off + err.Token.Value.Token.Length),
									buffer.Key,
									code,
									err.Phase +":" + err.Text + "\n" + err.ExtraText));
							}
							else diag.Add(new Diagnostic(sever,
								new Range(line, 0, line, 0),
								buffer.Key,
								code,
								err.Phase + ":" + err.Text + "\n" + err.ExtraText));
						}
					}
				}
			}

			return diag;
		}
	}
}
