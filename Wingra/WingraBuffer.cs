using Wingra.Parser;
using ILanguage;
using System;
using System.Collections.Generic;

namespace Wingra
{
	public class WingraBuffer : ITextBuffer
	{
		public string Key { get; set; }

		public List<string> _text = new List<string>();
		public List<LexLine> _lex = new List<LexLine>();
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

		public int Lines { get { return _text.Count; } }
		public bool Dirty { get; set; }

		public event LineChangedHandler evLineChanged;
		public event LineChangedHandler evLineInserted;
		public event LineChangedHandler evLineRemoved;
		public event LineUpdatedHandler evLineUpdated;

		public void AppendLine(string text)
		{
			_text.Add(text);
			_lex.Add(new LexLine(text, SpacesToIndent));
			evLineInserted?.Invoke(Lines - 1, text, "");
			Dirty = true;
		}

		public void DeleteLine(int line)
		{
			var prev = _text[line];
			_text.RemoveAt(line);
			_lex.RemoveAt(line);
			evLineRemoved?.Invoke(line, "", prev);
			Dirty = true;
		}

		public void InsertLine(int line, string text)
		{
			_text.Insert(line, text);
			_lex.Insert(line, new LexLine(text, SpacesToIndent));
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
			string text = _text[line];
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
			var prev = _text[line];
			_lex[line].Process(text, SpacesToIndent);
			evLineChanged?.Invoke(line, text, prev);
			Dirty = true;
		}

		public string TextAtLine(int line)
		{
			return _text[line];
		}

		public LexLine GetSyntaxMetadata(int line) => _lex[line];

		//includes line continuation
		// if you are in the middle of a line continuation, gets the line from the start
		public List<LexLine> GetCompleteLine(int line, out int startOfLine)
		{
			startOfLine = line;
			List<LexLine> cont = new List<LexLine>() { _lex[line] };
			if (line == _lex.Count - 1) return cont;
			while (_lex[startOfLine].LineIsContinuation) // scan upward for continuation
			{
				startOfLine--;
				cont.Insert(0, _lex[startOfLine]);
			}
			for (int i = line + 1; i < _lex.Count; i++) // now scan downwards until there are no more continuations
				if (_lex[i].LineIsContinuation)
					cont.Add(_lex[i]);
				else break;
			return cont;
		}

		#region Errors

		// TODO: previously, using FSL system allowed unique pointers that would auto-move when a line was inserted
		// now we don't have the same unique pointer, so the line numbers might get out of sync
		// probably doesn't matter unless I build my own editor again, though
		// maybe lexLine should just be a class instead of a struct? it does contain a list pointer that is maintained
		MapSet<int, SyntaxError> _errors = new MapSet<int, SyntaxError>();
		public IEnumerable<SyntaxError> GetErrorsAtLine(int line)
		{
			if (_errors.Contains(line))
				foreach (var er in _errors.Values(line))
					yield return er;
		}
		public void RegisterError(SyntaxError error)
		{
			if (error.Line < 0) return;
			_errors.Set(error.Line, error);
			evLineUpdated?.Invoke(error.Line);
		}
		public void ClearError(SyntaxError error)
		{
			_errors.RemoveValues(er => er == error);
		}
		#endregion

		public override string ToString()
		{
			return "File:" + ShortFileName;
		}

		//DOES NOT BROADCAST UPDATE EVENTS
		public void SyncFromExternal(List<string> lines)
		{
			var old = _text;
			_text = lines;
			if(_lex.Count > _text.Count)
				_lex.RemoveRange(_text.Count, _lex.Count - _text.Count);
			for (int i = 0; i < _text.Count; i++)
			{
				if (i >= old.Count)
					_lex.Add(new LexLine(_text[i], SpacesToIndent));
				else if (_text[i] != old[i])
					_lex[i] = _lex[i].Process(_text[i], SpacesToIndent);
			}
		}
	}
}
