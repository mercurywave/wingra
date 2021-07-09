using System;
using System.Collections.Generic;
using System.Text;

namespace ILanguage
{
	public delegate void LineUpdatedHandler(int line);
	public delegate void LineChangedHandler(int line, string newText, string oldText);
	public interface ITextBuffer
	{
		string Key { get; }
		int Lines { get; }
		string TextAtLine(int line);
		void SetLine(int line, string text);
		void DeleteLine(int line);
		void InsertLine(int line, string text);
		void AppendLine(string text);
		event LineChangedHandler evLineChanged; // text changed
		event LineUpdatedHandler evLineUpdated; // e.g. syntax highlighting changed
		event LineChangedHandler evLineInserted;
		event LineChangedHandler evLineRemoved;
		Tuple<int, int> ScanToken(int line, int idx, bool left = false);
		Tuple<int, int> ScanSection(int line, int idx, bool up = false);
		bool Dirty { get; set; }
	}
}
