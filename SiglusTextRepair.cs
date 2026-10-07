// File: SiglusTextRepair.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// The quoting repair an untouched Siglus script needs before English can be shown:
	/// the engine displays a nametag, a line of dialogue or a line of narration only when
	/// it is a quoted string, and the original scripts write Japanese text bare. Three
	/// cases, applied to every line, ported from the old normalize step:
	///
	///   1.  【name】      becomes  【"name"】   - an empty name is an error, left as it is
	///   2.  「speech」    becomes  "「speech」" - empty speech is allowed
	///   3.  a line that opens, at column zero, with a Japanese character or a full-width
	///       Latin letter is quoted whole, with an exact trailing "nl" or "r" kept outside
	///
	/// Quotes already at the edges of a bracket's content, or just outside the brackets,
	/// are taken off before exactly one is put on, so a line that was half-quoted by hand
	/// comes out the same as one never touched. Unmatched brackets are reported, not fixed.
	/// </summary>
	public static class SiglusTextRepair {

		private const char NametagOpen = '【';
		private const char NametagClose = '】';
		private const char SpeechOpen = '「';
		private const char SpeechClose = '」';


		/// <summary>
		/// Repairs one line's quoting.
		/// </summary>
		/// <param name="line">The line, without its ending.</param>
		/// <param name="number">Its 1-based line number, for messages.</param>
		/// <param name="warnings">Receives a line for an empty nametag or an unmatched bracket.</param>
		/// <returns>The repaired line, or the same line when nothing applied.</returns>
		public static string QuoteLine(string line, int number, List<string> warnings) {
			ReportUnmatched(line, number, NametagOpen, NametagClose, warnings);
			ReportUnmatched(line, number, SpeechOpen, SpeechClose, warnings);
			bool quoteWhole = line.Length > 0 && OpensJapanese(line[0]);
			string repaired = QuotePairs(line, number, NametagOpen, NametagClose, true, false, warnings);
			repaired = QuotePairs(repaired, number, SpeechOpen, SpeechClose, false, true, warnings);
			if (quoteWhole == true) {
				string suffix = "";
				if (repaired.EndsWith("nl", StringComparison.Ordinal) == true) {
					suffix = "nl";
				}
				if (suffix.Length == 0 && repaired.EndsWith("r", StringComparison.Ordinal) == true) {
					suffix = "r";
				}
				string body = repaired.Substring(0, repaired.Length - suffix.Length);
				repaired = QuoteKeepingOperators(body) + suffix;
			}
			return repaired;
		}


		/// <summary>
		/// Quotes bare text as one or more string literals, leaving an "nl" or "r" that sits
		/// between Japanese characters outside the quotes: it is the engine's line break or
		/// wait there, not prose, and inside a string it would be shown as letters. The
		/// split is exactly at the operator, so spaces around it stay with the text.
		/// </summary>
		///
		/// A string literal already inside the bare text - an ASCII character quoted so the
		/// engine shows it instead of obeying it, such as "-" - is folded into the string
		/// around it: its quotes go, its content stays verbatim. Quoting around it instead
		/// would turn its quotes inside out and hand the engine the operator it was hiding.
		/// </summary>
		/// <param name="text">Bare text, possibly holding quoted literals, no quotes of its own at the edges.</param>
		/// <returns>"text", or "left"nl"right" where an operator sat between text.</returns>
		public static string QuoteKeepingOperators(string text) {
			StringBuilder result = new();
			StringBuilder segment = new();
			bool inLiteral = false;
			int at = 0;
			while (at < text.Length) {
				char current = text[at];
				bool escapedQuote = inLiteral == true && current == '\\' && at + 1 < text.Length && text[at + 1] == '"';
				int length = 0;
				if (inLiteral == false) {
					length = OperatorLengthAt(text, at);
				}
				if (length > 0) {
					result.Append('"').Append(segment).Append('"');
					result.Append(text, at, length);
					segment.Clear();
					at += length;
				}
				if (length == 0 && escapedQuote == true) {
					segment.Append(current).Append('"');
					at += 2;
				}
				if (length == 0 && escapedQuote == false && current == '"') {
					inLiteral = inLiteral == false;
					at += 1;
				}
				if (length == 0 && escapedQuote == false && current != '"') {
					segment.Append(current);
					at += 1;
				}
			}
			result.Append('"').Append(segment).Append('"');
			return result.ToString();
		}


		/// <summary>
		/// The length of an "nl" or "r" at a position whose nearest non-space neighbours on
		/// both sides are text - a non-ASCII character or the edge of a quoted literal; 0
		/// when there is no such operator there.
		/// </summary>
		private static int OperatorLengthAt(string text, int at) {
			int length = 0;
			if (at + 1 < text.Length && text[at] == 'n' && text[at + 1] == 'l') {
				length = 2;
			}
			if (length == 0 && text[at] == 'r') {
				length = 1;
			}
			if (length > 0) {
				int before = at - 1;
				while (before >= 0 && text[before] == ' ') {
					before -= 1;
				}
				int after = at + length;
				while (after < text.Length && text[after] == ' ') {
					after += 1;
				}
				bool textBefore = before >= 0 && ((int)text[before] > 127 || text[before] == '"');
				bool textAfter = after < text.Length && ((int)text[after] > 127 || text[after] == '"');
				if (textBefore == false || textAfter == false) {
					length = 0;
				}
			}
			return length;
		}


		/// <summary>
		/// Whether a character is one the old rule quoted a whole line for: kanji, hiragana,
		/// katakana, the iteration marks, or a full-width Latin letter.
		/// </summary>
		public static bool OpensJapanese(char character) {
			int code = (int)character;
			bool kanji = (code >= 0x3400 && code <= 0x4DBF) || (code >= 0x4E00 && code <= 0x9FFF) || (code >= 0xF900 && code <= 0xFAFF);
			bool hiragana = (code >= 0x3041 && code <= 0x3096) || (code >= 0x309D && code <= 0x309F);
			bool katakana = (code >= 0x30A1 && code <= 0x30FA) || (code >= 0x30FC && code <= 0x30FF) || (code >= 0x31F0 && code <= 0x31FF);
			bool marks = code >= 0x3005 && code <= 0x3007;
			bool fullWidthLatin = (code >= 0xFF21 && code <= 0xFF3A) || (code >= 0xFF41 && code <= 0xFF5A);
			return kanji || hiragana || katakana || marks || fullWidthLatin;
		}


		/// <summary>
		/// Rewrites every open...close pair on a line whose content holds no nested open or
		/// close: quotes go inside the brackets or outside them, and any quotes already at
		/// those edges are taken off first.
		/// </summary>
		private static string QuotePairs(string line, int number, char open, char close, bool quoteInside, bool allowEmpty, List<string> warnings) {
			StringBuilder result = new();
			int at = 0;
			while (at < line.Length) {
				int openAt = line.IndexOf(open, at);
				bool found = false;
				if (openAt >= 0) {
					int closeAt = line.IndexOf(close, openAt + 1);
					int nextOpen = line.IndexOf(open, openAt + 1);
					bool clean = closeAt >= 0 && (nextOpen < 0 || nextOpen > closeAt);
					// Quotes immediately before the open belong to the pair; the inside test
					// looks at the position before them, so a pair that is itself quoted is
					// not mistaken for one sitting inside a string.
					int start = openAt;
					while (start > at && line[start - 1] == '"') {
						start -= 1;
					}
					// A pair inside a quoted string is part of that string's text - a narrator
					// quoting a word - and is left alone. This is what makes the repair safe
					// to run on a script that was prepared already.
					if (clean == true && InsideString(line, start) == true) {
						result.Append(line, at, closeAt + 1 - at);
						at = closeAt + 1;
						found = true;
					}
					if (clean == true && found == false) {
						// Quotes immediately after the close belong to the pair too.
						int end = closeAt + 1;
						while (end < line.Length && line[end] == '"') {
							end += 1;
						}
						string body = StripEdgeQuotes(line.Substring(openAt + 1, closeAt - openAt - 1));
						bool empty = body.Trim().Length == 0;
						result.Append(line, at, start - at);
						// Speech already quoted on the outside is finished work, whatever
						// its content holds - literals, operators - and is left exactly as
						// it is, so a prepared script passes through unchanged.
						bool alreadyOutside = quoteInside == false && start < openAt && end > closeAt + 1;
						if (alreadyOutside == true) {
							result.Append(line, start, end - start);
							empty = false;
							allowEmpty = true;
						}
						if (alreadyOutside == false && empty == true && allowEmpty == false) {
							warnings.Add("line " + number + ": no text inside " + open + close + "; left as it is.");
							result.Append(line, start, end - start);
						}
						if (alreadyOutside == false && (empty == false || allowEmpty == true)) {
							if (quoteInside == true) {
								result.Append(open).Append('"').Append(body).Append('"').Append(close);
							}
							if (quoteInside == false) {
								result.Append(QuoteKeepingOperators(open + body + close));
							}
						}
						at = end;
						found = true;
					}
				}
				if (found == false) {
					result.Append(line, at, line.Length - at);
					at = line.Length;
				}
			}
			return result.ToString();
		}


		/// <summary>
		/// Whether a position on a line sits inside a quoted string: an odd number of
		/// unescaped quotes come before it.
		/// </summary>
		private static bool InsideString(string line, int index) {
			bool inside = false;
			for (int at = 0; at < index; at++) {
				bool escaped = at > 0 && line[at - 1] == '\\';
				if (line[at] == '"' && escaped == false) {
					inside = inside == false;
				}
			}
			return inside;
		}


		/// <summary>
		/// Takes straight quotes off the edges of a bracket's content, leaving the
		/// whitespace there and any quotes inside the text.
		/// </summary>
		private static string StripEdgeQuotes(string text) {
			int start = 0;
			while (start < text.Length && (text[start] == '"' || char.IsWhiteSpace(text[start]))) {
				start += 1;
			}
			int end = text.Length;
			while (end > start && (text[end - 1] == '"' || char.IsWhiteSpace(text[end - 1]))) {
				end -= 1;
			}
			string leading = text.Substring(0, start).Replace("\"", "");
			string trailing = text.Substring(end).Replace("\"", "");
			return leading + text.Substring(start, end - start) + trailing;
		}


		/// <summary>
		/// Reports each bracket on a line that has no partner, by column.
		/// </summary>
		private static void ReportUnmatched(string line, int number, char open, char close, List<string> warnings) {
			List<int> opens = new();
			for (int column = 0; column < line.Length; column++) {
				if (line[column] == open) {
					opens.Add(column + 1);
				}
				if (line[column] == close) {
					bool hasPartner = opens.Count > 0;
					if (hasPartner == true) {
						opens.RemoveAt(opens.Count - 1);
					}
					if (hasPartner == false) {
						warnings.Add("line " + number + ", column " + (column + 1) + ": unmatched closing bracket " + close + ".");
					}
				}
			}
			foreach (int column in opens) {
				warnings.Add("line " + number + ", column " + column + ": unmatched opening bracket " + open + ".");
			}
		}
	}
}
