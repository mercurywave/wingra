using Wingra.Parser;
using ILanguage;
using System;
using System.Collections.Generic;

namespace Wingra
{
	public class WingraBuffer : ITextBuffer
	{
		public string Key { get; set; }

		public List<FileSyntaxLine> _lines = new List<FileSyntaxLine>();
		public int? ParseDebugLine = null;
		public int? IdentifierProcessDebugLine = null;
		STopOfFile _tree;

		public const int SpacesToIndent = 2;

		public STopOfFile SyntaxTree => _tree;

		public string ShortFileName => util.GetShortFileName(Key);

		public WingraBuffer(string key)
		{
			_tree = new STopOfFile();
			Key = key;
		}

		public int Lines { get { return _lines.Count; } }
		public bool Dirty { get; set; }

		public event LineChangedHandler evLineChanged;
		public event LineChangedHandler evLineInserted;
		public event LineChangedHandler evLineRemoved;
		public event LineUpdatedHandler evLineUpdated;

		public void AppendLine(string text)
		{
			_lines.Add(new FileSyntaxLine(text));
			evLineInserted?.Invoke(Lines - 1, text, "");
			Dirty = true;
		}

		public void DeleteLine(int line)
		{
			var prev = _lines[line].Text;
			_lines.RemoveAt(line);
			evLineRemoved?.Invoke(line, "", prev);
			Dirty = true;
		}

		public void InsertLine(int line, string text)
		{
			_lines.Insert(line, new FileSyntaxLine(text));
			evLineInserted?.Invoke(line, text, "");
			Dirty = true;
		}

		public void Clear()
		{
			for (int i = Lines - 1; i >= 0; i--)
				DeleteLine(i);
		}

		public Tuple<int, int> ScanSection(int line, int idx, bool up = false)
		{
			if (up) line = Math.Max(0, line - 20);
			else line = Math.Min(0, line + 20);
			return new Tuple<int, int>(line, idx);
		}

		public Tuple<int, int> ScanToken(int line, int idx, bool left = false)
		{
			int mod = (left ? -1 : 1);
			int relevantChar = (left ? 0 : -1);
			bool startedWord = false;
			string splits = " -,.()[]{};:";
			string text = _lines[line].Text;
			int x = idx;
			for (int i = 0; i < text.Length; i++)
			{
				if (x + mod < 0 || x + mod > text.Length) break;
				x += mod;
				int rel = x + relevantChar;
				if (rel < 0 || rel >= text.Length) break;
				string c = "" + text[rel]; //chars are weird
				bool matchSplit = splits.Contains(c);
				if (startedWord && matchSplit) break;
				if (!matchSplit) startedWord = true;
			}
			return new Tuple<int, int>(x, line);
		}

		public void SetLine(int line, string text)
		{
			var prev = _lines[line].Text;
			_lines[line].Modify(text);
			evLineChanged?.Invoke(line, text, prev);
			Dirty = true;
		}

		public string TextAtLine(int line)
		{
			return _lines[line].Text;
		}

		public FileSyntaxLine GetSyntaxMetadata(int line) => _lines[line];

		//includes line continuation
		// if you are in the middle of a line continuation, gets the line from the start
		public List<LexLine> GetCompleteLine(int line, out int startOfLine)
		{
			startOfLine = line;
			FileSyntaxLine syn = _lines[line];
			List<LexLine> cont = new List<LexLine>() { syn.Lex };
			if (line == _lines.Count - 1) return cont;
			while (_lines[startOfLine].Lex.LineIsContinuation) // scan upward for continuation
			{
				startOfLine--;
				cont.Insert(0, _lines[startOfLine].Lex);
			}
			for (int i = line + 1; i < _lines.Count; i++) // now scan downwards until there are no more continuations
				if (_lines[i].Lex.LineIsContinuation)
					cont.Add(_lines[i].Lex);
				else break;
			return cont;
		}

		#region Errors
		struct LineError
		{
			public FileSyntaxLine Syntax;
			public SyntaxError Error;
			public LineError(FileSyntaxLine syn, SyntaxError err)
			{
				Syntax = syn;
				Error = err;
			}
		}
		List<LineError> _errorMap = new List<LineError>();
		public IEnumerable<SyntaxError> GetErrorsAtLine(FileSyntaxLine line)
		{
			foreach (var le in _errorMap)
				if (le.Syntax == line)
					yield return le.Error;
		}
		public void RegisterError(SyntaxError error)
		{
			if (error.Line < 0) return;
			_errorMap.Add(new LineError(GetSyntaxMetadata(error.Line), error));
			evLineUpdated?.Invoke(error.Line);
		}
		public void ClearError(SyntaxError error)
		{
			_errorMap.RemoveAll(le => le.Error == error);
		}
		#endregion

		#region code suggest
		public List<String> GenericResultsForLine(SyntaxNode node)
		{
			List<string> results = new List<string>();
			if (node != null)
			{
				searchAddTypeDefs(results);
				searchAddControlFlow(results);
			}
			return results;
		}
		void searchAddTypeDefs(List<string> results)
		{
			results.Add("int");
			results.Add("float");
			results.Add("string");
		}
		void searchAddControlFlow(List<string> results)
		{
			results.Add("if");
			results.Add("else");
			results.Add("switch");
			results.Add("for");
			results.Add("while");
			results.Add("until");
		}
		#endregion

		public override string ToString()
		{
			return "File:" + ShortFileName;
		}
	}

	//provides reflection of files for editor and detailed error messages
	// I don't know if I really need any of this with Kilt...
	public class FileSyntaxLine
	{
		LexLine _lex;
		string _text;
		public FileSyntaxLine(string text)
		{
			_text = text;
			_lex = new LexLine(text, WingraBuffer.SpacesToIndent);
		}

		public void Modify(string newText)
		{
			_text = newText;
			_lex.Process(newText, WingraBuffer.SpacesToIndent);
		}

		public LexLine Lex => _lex;
		public string Text => _text;
	}
}
