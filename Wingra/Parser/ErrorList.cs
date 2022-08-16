using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wingra.Parser
{
	public class ErrorList
	{
		List<SyntaxError> _errors = new List<SyntaxError>();

		public void Clear()
		{
			int prev = _errors.Count;
			foreach (var err in _errors)
				if (err.Buffer != null)
					err.Buffer.ClearError(err);
			_errors.Clear();
			if (_errors.Count != prev) evModified?.Invoke(_errors.Count);
		}
		public void ClearForFile(WingraBuffer buffer)
		{
			int prev = _errors.Count;
			foreach (var err in _errors)
				if (err.Buffer == buffer)
					buffer.ClearError(err);
			_errors.RemoveAll(e => e.Buffer == buffer);
			if (_errors.Count != prev) evModified?.Invoke(_errors.Count);
		}

		public void ClearForLaterPhases(ePhase phase)
		{
			int prev = _errors.Count;
			foreach (var err in _errors)
				if (phase >= err.Phase)
					err.Buffer.ClearError(err);
			_errors.RemoveAll(e => e.Phase >= phase);
			if (_errors.Count != prev) evModified?.Invoke(_errors.Count);
		}

		public void LogError(string text, WingraBuffer buffer, ePhase phase, int line = -1, RelativeTokenReference? token = null, eErrorType type = eErrorType.Error, string extraText = "")
		{
			if (token.HasValue)
				line += token.Value.SubLine;
			var err = new SyntaxError(text, buffer, phase, line, token, type, extraText);
			_errors.Add(err);
			if (buffer != null)
				buffer.RegisterError(err);
			evModified?.Invoke(_errors.Count);
		}

		public IEnumerable<SyntaxError> Errors => _errors;
		public event Action<int> evModified;

		public ErrorLogger GetFileLogger(WingraBuffer buffer) => new FileErrorLogger(this, buffer);
		public ErrorLogger GetLogger() => new FileErrorLogger(this, null);
	}

	public abstract class ErrorLogger
	{
		public virtual void LogError(string text, ePhase phase, int line, RelativeTokenReference? token = null, eErrorType type = eErrorType.Error, string extraText = "")
		{
		}
		public virtual bool AnyLogged => throw new NotImplementedException();
		public override string ToString() => "Errors: " + AnyLogged;
	}

	public class FileErrorLogger : ErrorLogger
	{
		public ErrorList Master;
		WingraBuffer _buffer;
		public FileErrorLogger(ErrorList master, WingraBuffer buffer) : base()
		{
			Master = master;
			_buffer = buffer;
		}
		public override void LogError(string text, ePhase phase, int line = -1, RelativeTokenReference? token = null, eErrorType type = eErrorType.Error, string extraText = "")
		{
			Master.LogError(text, _buffer, phase, line, token, type, extraText);
		}
		public override bool AnyLogged => Master.Errors.Any();
		public override string ToString() => "Errors: " + Master.Errors.Count();
	}

	public class MinimalErrorLogger : ErrorLogger
	{
		public bool EncounteredError = false;
		public string FirstError = "";
		public override void LogError(string text, ePhase phase, int line = -1, RelativeTokenReference? token = null, eErrorType type = eErrorType.Error, string extraText = "")
		{
			EncounteredError = true;
			if (FirstError == "")
				FirstError = text;
		}
		public override bool AnyLogged => EncounteredError;
	}

	public enum eErrorType { Error, Warning, Test };
	public enum ePhase { PreParse, Macros, Parse, Compile, Emit, Other };
	public class SyntaxError
	{
		public string Text;
		public WingraBuffer Buffer;
		public ePhase Phase;
		public int Line;
		public RelativeTokenReference? Token;
		public eErrorType Type;
		public string ExtraText = "";
		public SyntaxError(string text, WingraBuffer buffer, ePhase phase, int line = -1, RelativeTokenReference? token = null, eErrorType type = eErrorType.Error, string extraText = "")
		{
			Text = text;
			Buffer = buffer;
			Phase = phase;
			Line = line;
			Token = token;
			Type = type;
			ExtraText = extraText;
		}
	}

	class ParserException : Exception
	{
		public RelativeTokenReference? Token;
		public eErrorType Type; // you probably shouldn't ever throw on a warning, but hey...
		public ParserException(string str, RelativeTokenReference? token = null, eErrorType type = eErrorType.Error) : base(str)
		{
			Token = token;
			Type = type;
		}
	}
	class CompilerException : Exception
	{
		public int Line;
		public RelativeTokenReference? Token;
		public eErrorType Type; // you probably shouldn't ever throw on a warning, but hey...
		public CompilerException(string str, int line, RelativeTokenReference? token = null, eErrorType type = eErrorType.Error) : base(str)
		{
			Line = line;
			Token = token;
			Type = type;
		}
	}
}
