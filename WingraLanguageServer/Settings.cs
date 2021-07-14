using System;
using System.Collections.Generic;
using System.Text;

namespace WingraLanguageServer
{
	public class SettingsRoot
	{
		public LanguageServerSettings LanguageServer { get; set; }
	}

	public class LanguageServerSettings
	{
		public int MaxNumberOfProblems { get; set; } = 10;

		public LanguageServerTraceSettings Trace { get; } = new LanguageServerTraceSettings();
	}

	public class LanguageServerTraceSettings
	{
		public string Server { get; set; }
	}
}
