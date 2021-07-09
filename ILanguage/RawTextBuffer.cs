using System;
using System.Collections.Generic;
using System.Text;

namespace ILanguage
{
	public class RawTextBuffer : ITextBuffer
	{
		public string Key { get; set; }
		public List<string> _lines = new List<string>();
		public bool Dirty { get; set; }

		public int Lines { get { return _lines.Count; } }

		public event LineChangedHandler evLineChanged;
		public event LineChangedHandler evLineInserted;
		public event LineChangedHandler evLineRemoved;
		public event LineUpdatedHandler evLineUpdated;

		public RawTextBuffer(string name = "")
		{
			Key = name;
		}

		public void AppendLine(string text)
		{
			_lines.Add(text);
			evLineInserted?.Invoke(Lines - 1, text, "");
			Dirty = true;
		}

		public void DeleteLine(int line)
		{
			var prev = _lines[line];
			_lines.RemoveAt(line);
			evLineRemoved?.Invoke(line, "", prev);
			Dirty = true;
		}

		public void InsertLine(int line, string text)
		{
			_lines.Insert(line, text);
			evLineInserted?.Invoke(line, text, "");
			Dirty = true;
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
			string text = _lines[line];
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
			var prev = _lines[line];
			_lines[line] = text;
			evLineChanged?.Invoke(line, text, prev);
			Dirty = true;
		}

		public string TextAtLine(int line)
		{
			if (line > Lines) return "";
			return _lines[line];
		}

		public void Clear()
		{
			while (Lines > 0)
				DeleteLine(0);
		}
	}
}
