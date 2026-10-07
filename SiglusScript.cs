// File: SiglusScript.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// One physical line of a .ss file: its text, and the exact line ending that followed
	/// it. The ending is empty for a final line with no trailing break.
	/// </summary>
	public class ScriptLine {

		/// <summary>The line's text, without its line ending.</summary>
		public string Content = "";

		/// <summary>The line ending that followed, verbatim: "\r\n", "\n", "\r" or "".</summary>
		public string Ending = "";
	}


	/// <summary>
	/// One parsed dialogue-file entry: its id, its optional nametag and its run.
	/// </summary>
	public class DialogueEntry {

		/// <summary>The number in the ::NNNNNNNNNN:: token, scoped to its label.</summary>
		public int Id = 0;

		/// <summary>Nametag content between the brackets, verbatim, quotes included. In Siglus those quotes are syntax.</summary>
		public string Nametag = "";

		/// <summary>Whether the entry opens with a bracketed nametag at all.</summary>
		public bool HasNametag = false;

		/// <summary>Everything after the nametag, verbatim.</summary>
		public string Text = "";
	}


	/// <summary>
	/// How a SiglusEngine .ss script is read, written and cut: which lines carry text,
	/// where a text run ends, what a label is, and the shapes of the id token, the
	/// dialogue entry and the pointer comment that Split writes and Join reads. Pure
	/// rules; no file is split or joined here.
	///
	/// Two things about the cut that were expensive to get right, kept from the old tool:
	///
	/// 1. Classify on the TRIMMED line. Every Siglus operator, keyword, brace and sigil is
	///    ASCII; every text line is quoted or opens with CJK. Testing the raw first
	///    character reads an indented text line as code, which hid 238 tab-indented
	///    selbtn/select argument lines from every survey before 2026-09-10.
	/// 2. The run ends where the TRAILING OPERATOR RUN starts, not where a quote closes.
	///    Lines shaped dash dash "text" punct r carry bare text on both sides of a quoted
	///    fragment, and a quoted-run rule strands it.
	/// </summary>
	public static class SiglusScript {

		/// <summary>Opening nametag bracket, U+3010. An escape, not a literal: the sources carry no BOM.</summary>
		public const string NametagOpen = "【";

		/// <summary>Closing nametag bracket, U+3011.</summary>
		public const string NametagClose = "】";

		/// <summary>Name of the dialogue subfolder under split\.</summary>
		public const string DialoguesFolder = "dialogues";

		/// <summary>File extension every script carries.</summary>
		public const string ScriptExtension = ".ss";

		/// <summary>File extension every dialogue file carries.</summary>
		public const string DialogueExtension = ".txt";

		/// <summary>Comment that opens a choice block. Matched as a prefix, so a trailing label is free.</summary>
		public const string ChoiceStart = "//start choices";

		/// <summary>Comment that closes a choice block.</summary>
		public const string ChoiceEnd = "//end choices";

		/// <summary>Opens a line comment. A joined line that carries a fault is commented with this.</summary>
		public const string CommentMarker = "//";

		/// <summary>Length of an id token: "::" + 10 digits + "::".</summary>
		public const int TokenLength = 14;

		/// <summary>Dialogue files are CRLF, and the last entry carries no trailing break.</summary>
		public const string EntryBreak = "\r\n";

		/// <summary>The break written after a pointer whose label line had none.</summary>
		public const string FallbackBreak = "\r\n";

		private const string TokenFence = "::";
		private const int TokenDigits = 10;
		private const string PointerHead = "// .\\";

		/// <summary>
		/// CP932 with BOTH fallbacks throwing. A silently substituting encoder turns an
		/// unencodable character into '?' and the damage only surfaces in-game. An em dash,
		/// an en dash and the minus sign are all unencodable, and an editor produces them
		/// without being asked.
		/// </summary>
		public static readonly Encoding ScriptEncoding = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);


		/// <summary>
		/// Reads a .ss or dialogue file as CP932. Throws rather than substituting a character
		/// it cannot decode.
		/// </summary>
		/// <param name="path">The file to read.</param>
		/// <returns>The file's text, line endings intact.</returns>
		public static string ReadScript(string path) {
			return ScriptEncoding.GetString(File.ReadAllBytes(path));
		}


		/// <summary>
		/// Writes text as CP932, creating the folder if needed. Encodes BEFORE touching the
		/// file, so an unencodable character leaves the destination as it was.
		/// </summary>
		/// <param name="path">The file to write.</param>
		/// <param name="text">The text to write, line endings included.</param>
		public static void WriteScript(string path, string text) {
			byte[] raw = ScriptEncoding.GetBytes(text);
			string folder = Path.GetDirectoryName(path) ?? "";
			if (folder.Length > 0) {
				Directory.CreateDirectory(folder);
			}
			File.WriteAllBytes(path, raw);
		}


		/// <summary>
		/// Breaks text into lines, keeping each line's own ending verbatim so the file can
		/// be rebuilt byte for byte.
		/// </summary>
		/// <param name="text">The text to break up.</param>
		/// <returns>The lines, in order.</returns>
		public static List<ScriptLine> ReadLines(string text) {
			List<ScriptLine> lines = new();
			int start = 0;
			int at = 0;
			while (at < text.Length) {
				int breakLength = BreakLengthAt(text, at);
				if (breakLength == 0) {
					at += 1;
				}
				if (breakLength > 0) {
					ScriptLine line = new();
					line.Content = text.Substring(start, at - start);
					line.Ending = text.Substring(at, breakLength);
					lines.Add(line);
					at += breakLength;
					start = at;
				}
			}
			// A final line with no trailing break is still a line.
			if (start < text.Length) {
				ScriptLine last = new();
				last.Content = text.Substring(start);
				last.Ending = "";
				lines.Add(last);
			}
			return lines;
		}


		/// <summary>
		/// Reassembles lines into one string, each line followed by its own ending.
		/// </summary>
		/// <param name="lines">The lines to reassemble.</param>
		/// <returns>The joined text.</returns>
		public static string WriteLines(List<ScriptLine> lines) {
			StringBuilder text = new();
			foreach (ScriptLine line in lines) {
				text.Append(line.Content);
				text.Append(line.Ending);
			}
			return text.ToString();
		}


		/// <summary>
		/// Decides whether a line carries translatable text: trimmed, it opens with a quote
		/// or a non-ASCII character, or it carries bare non-ASCII in an argument position
		/// (selbtn writes its options as bare unquoted literals).
		/// </summary>
		/// <param name="line">The raw line, indentation included.</param>
		/// <returns>True when the line carries text.</returns>
		public static bool IsTextLine(string line) {
			string trimmed = line.TrimStart(' ', '\t');
			bool isText = false;
			if (trimmed.Length > 0) {
				if (trimmed[0] == '"') {
					isText = true;
				}
				if ((int)trimmed[0] > 127) {
					isText = true;
				}
				// The trigger is deliberately "carries bare non-ASCII", NOT "the command is
				// selbtn": naming the command would silently drop the next command that
				// does the same thing.
				if (isText == false && HasBareNonAscii(line) == true) {
					isText = true;
				}
			}
			return isText;
		}


		/// <summary>Whether a line opens a choice block.</summary>
		public static bool IsChoiceStart(string line) {
			return line.TrimStart(' ', '\t').StartsWith(ChoiceStart, StringComparison.Ordinal);
		}


		/// <summary>Whether a line closes a choice block.</summary>
		public static bool IsChoiceEnd(string line) {
			return line.TrimStart(' ', '\t').StartsWith(ChoiceEnd, StringComparison.Ordinal);
		}


		/// <summary>
		/// Whether a line carries a quoted string outside every comment.
		/// </summary>
		/// <param name="line">The raw line.</param>
		/// <returns>True when a string literal opens somewhere on the line.</returns>
		public static bool HasQuotedString(string line) {
			bool inBlock = false;
			bool found = false;
			int at = 0;
			while (at < line.Length && found == false) {
				char current = line[at];
				bool hasNext = at + 1 < line.Length;
				int step = 1;
				bool wasInBlock = inBlock;
				if (wasInBlock == true) {
					if (current == '*' && hasNext == true && line[at + 1] == '/') {
						inBlock = false;
						step = 2;
					}
				}
				if (wasInBlock == false) {
					if (current == ';') {
						step = line.Length;
					}
					if (current == '/' && hasNext == true && line[at + 1] == '/') {
						step = line.Length;
					}
					if (current == '/' && hasNext == true && line[at + 1] == '*') {
						inBlock = true;
						step = 2;
					}
					if (current == '"') {
						found = true;
					}
				}
				at += step;
			}
			return found;
		}


		/// <summary>
		/// Decides text-or-code for every line of a script at once, so the choice-block
		/// state machine exists in exactly one place. Inside a choice block a quoted string
		/// is an option rather than an asset name; outside one it is not text.
		/// </summary>
		/// <param name="lines">Every line of one script, in order.</param>
		/// <returns>One flag per line, true where the line carries text.</returns>
		public static List<bool> ClassifyLines(List<ScriptLine> lines) {
			List<bool> flags = new();
			bool inChoice = false;
			foreach (ScriptLine line in lines) {
				// The end stamp closes before classifying and the start stamp opens after,
				// so neither stamp is ever inside its own region.
				if (IsChoiceEnd(line.Content) == true) {
					inChoice = false;
				}
				bool isText = IsTextLine(line.Content);
				if (inChoice == true && isText == false) {
					if (HasQuotedString(line.Content) == true) {
						isText = true;
					}
					if (HasBareNonAscii(line.Content) == true) {
						isText = true;
					}
				}
				flags.Add(isText);
				if (IsChoiceStart(line.Content) == true) {
					inChoice = true;
				}
			}
			return flags;
		}


		/// <summary>
		/// Whether a line carries a non-ASCII character outside every quoted string and
		/// every comment: text written bare in an argument position. The .ss dialect keeps
		/// NScripter's ';' comment as well as '//' and '/* */'.
		/// </summary>
		/// <param name="line">The raw line.</param>
		/// <returns>True when bare text is present.</returns>
		public static bool HasBareNonAscii(string line) {
			bool inString = false;
			bool inBlock = false;
			bool found = false;
			int at = 0;
			while (at < line.Length && found == false) {
				char current = line[at];
				bool hasNext = at + 1 < line.Length;
				int step = 1;
				bool wasInBlock = inBlock;
				if (wasInBlock == true) {
					if (current == '*' && hasNext == true && line[at + 1] == '/') {
						inBlock = false;
						step = 2;
					}
				}
				if (wasInBlock == false) {
					bool handled = false;
					// An escaped quote is content, not a boundary.
					if (current == '\\' && hasNext == true && line[at + 1] == '"') {
						step = 2;
						handled = true;
					}
					if (handled == false && current == '"') {
						inString = inString == false;
						handled = true;
					}
					// Comments run to end of line, so nothing after one can be bare.
					if (handled == false && inString == false && current == ';') {
						step = line.Length;
						handled = true;
					}
					if (handled == false && inString == false && current == '/' && hasNext == true && line[at + 1] == '/') {
						step = line.Length;
						handled = true;
					}
					if (handled == false && inString == false && current == '/' && hasNext == true && line[at + 1] == '*') {
						inBlock = true;
						step = 2;
						handled = true;
					}
					if (handled == false && inString == false && (int)current > 127) {
						found = true;
					}
				}
				at += step;
			}
			return found;
		}


		/// <summary>
		/// A line's leading spaces and tabs. Indentation belongs to the .ss, never to the
		/// text run.
		/// </summary>
		public static string LeadingWhitespace(string line) {
			int at = 0;
			while (at < line.Length && (line[at] == ' ' || line[at] == '\t')) {
				at += 1;
			}
			return line.Substring(0, at);
		}


		/// <summary>
		/// Strips the trailing operator run - "r", "nl", "//nl", "page" and the whitespace
		/// around them - off a line body, repeatedly, and returns what is left.
		/// </summary>
		/// <param name="body">The line with its indentation already removed.</param>
		/// <returns>The translatable run.</returns>
		public static string PeelTail(string body) {
			string run = body;
			bool peeled = true;
			while (peeled == true) {
				peeled = TryPeelOneOperator(run, out string shorter);
				if (peeled == true) {
					run = shorter;
				}
			}
			return run;
		}


		/// <summary>
		/// Reads the label name off a "#LABEL" line. Only a bare "#token" line is a label; a
		/// '#' inside a comment, and a "#ffffff" colour literal, carry a space somewhere.
		/// </summary>
		/// <param name="line">The raw line.</param>
		/// <returns>The label name without its '#', or empty when the line is not a label.</returns>
		public static string LabelOf(string line) {
			string label = "";
			string trimmed = line.Trim();
			if (trimmed.Length >= 2 && trimmed[0] == '#') {
				string rest = trimmed.Substring(1).Trim();
				bool hasGap = rest.Contains(' ') == true || rest.Contains('\t') == true;
				if (rest.Length > 0 && hasGap == false) {
					label = rest;
				}
			}
			return label;
		}


		/// <summary>
		/// Whether a text line is a selbtn / select argument list: a comma sits outside
		/// every quoted region.
		/// </summary>
		public static bool HasCommaOutsideQuotes(string line) {
			bool inside = false;
			bool found = false;
			int at = 0;
			while (at < line.Length && found == false) {
				char current = line[at];
				int step = 1;
				bool handled = false;
				if (current == '\\' && at + 1 < line.Length && line[at + 1] == '"') {
					step = 2;
					handled = true;
				}
				if (handled == false && current == '"') {
					inside = inside == false;
					handled = true;
				}
				if (handled == false && current == ',' && inside == false) {
					found = true;
				}
				at += step;
			}
			return found;
		}


		/// <summary>
		/// Formats a dialogue id as its "::0000000000::" token.
		/// </summary>
		public static string TokenFor(int id) {
			return TokenFence + id.ToString().PadLeft(TokenDigits, '0') + TokenFence;
		}


		/// <summary>
		/// Reads the id token a line opens with, after any indentation.
		/// </summary>
		/// <param name="line">The raw line.</param>
		/// <param name="id">The id that was read, or -1.</param>
		/// <returns>True when the line opens with a well-formed id token.</returns>
		public static bool TryReadToken(string line, out int id) {
			id = -1;
			bool read = false;
			string body = line.Substring(LeadingWhitespace(line).Length);
			if (body.Length >= TokenLength) {
				string token = body.Substring(0, TokenLength);
				bool fenced = token.StartsWith(TokenFence) == true && token.EndsWith(TokenFence) == true;
				if (fenced == true && int.TryParse(token.Substring(2, TokenDigits), out int parsed) == true) {
					id = parsed;
					read = true;
				}
			}
			return read;
		}


		/// <summary>
		/// Parses one dialogue-file line into its id, optional nametag and run.
		/// </summary>
		/// <param name="line">The raw line from a dialogue file.</param>
		/// <param name="entry">The parsed entry on success.</param>
		/// <returns>True when the line is an indexed entry.</returns>
		public static bool TryReadEntry(string line, out DialogueEntry entry) {
			entry = new DialogueEntry();
			bool parsed = TryReadToken(line, out int id);
			if (parsed == true) {
				string rest = line.Substring(TokenLength);
				if (rest.StartsWith(NametagOpen) == true && rest.Contains(NametagClose) == true) {
					int close = rest.IndexOf(NametagClose, StringComparison.Ordinal);
					entry.Nametag = rest.Substring(NametagOpen.Length, close - NametagOpen.Length);
					entry.HasNametag = true;
					rest = rest.Substring(close + NametagClose.Length);
				}
				entry.Id = id;
				entry.Text = rest;
			}
			return parsed;
		}


		/// <summary>
		/// Writes an entry back as a dialogue-file line. The exact inverse of TryReadEntry.
		/// </summary>
		public static string BuildEntry(DialogueEntry entry) {
			string nametag = "";
			if (entry.HasNametag == true) {
				nametag = NametagOpen + entry.Nametag + NametagClose;
			}
			return TokenFor(entry.Id) + nametag + entry.Text;
		}


		/// <summary>
		/// Reads the dialogue file name a pointer comment points at, without its extension.
		/// </summary>
		/// <param name="line">The raw line.</param>
		/// <returns>The file key, or empty when the line is not a pointer.</returns>
		public static string PointerTarget(string line) {
			string key = "";
			string trimmed = line.Trim();
			string wanted = PointerHead + DialoguesFolder + "\\";
			if (trimmed.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) == true) {
				string name = trimmed.Substring(wanted.Length).Trim();
				if (name.EndsWith(DialogueExtension, StringComparison.OrdinalIgnoreCase) == true) {
					key = name.Substring(0, name.Length - DialogueExtension.Length);
				}
			}
			return key;
		}


		/// <summary>
		/// Builds the pointer comment that sits under a text-bearing label.
		/// </summary>
		/// <param name="fileKey">The dialogue file's name without its extension.</param>
		public static string PointerFor(string fileKey) {
			return PointerHead + DialoguesFolder + "\\" + fileKey + DialogueExtension;
		}


		/// <summary>
		/// How long the line break at a position is: 2 for CRLF, 1 for CR or LF, 0 for none.
		/// </summary>
		private static int BreakLengthAt(string text, int at) {
			int length = 0;
			char current = text[at];
			if (current == '\r') {
				length = 1;
				if (at + 1 < text.Length && text[at + 1] == '\n') {
					length = 2;
				}
			}
			if (current == '\n') {
				length = 1;
			}
			return length;
		}


		/// <summary>
		/// Peels one trailing operator - nl, r, or a whitespace-bounded page - with an
		/// optional "//" and the whitespace around it. There is deliberately no left
		/// boundary guard for r and nl: a survey of the whole checkpoint found no line
		/// that peels differently with one. page needs an actual whitespace boundary.
		/// </summary>
		private static bool TryPeelOneOperator(string body, out string shorter) {
			shorter = body;
			int end = body.Length;
			while (end > 0 && (body[end - 1] == ' ' || body[end - 1] == '\t')) {
				end -= 1;
			}
			int token = -1;
			if (end >= 2 && body[end - 2] == 'n' && body[end - 1] == 'l') {
				token = end - 2;
			}
			if (token == -1 && end >= 1 && body[end - 1] == 'r') {
				token = end - 1;
			}
			bool pageWord = end >= 5 && body[end - 4] == 'p' && body[end - 3] == 'a' && body[end - 2] == 'g' && body[end - 1] == 'e';
			if (token == -1 && pageWord == true && (body[end - 5] == ' ' || body[end - 5] == '\t')) {
				token = end - 4;
			}
			bool peeled = false;
			if (token > 0) {
				int at = token;
				if (at >= 2 && body[at - 1] == '/' && body[at - 2] == '/') {
					at -= 2;
				}
				while (at > 0 && (body[at - 1] == ' ' || body[at - 1] == '\t')) {
					at -= 1;
				}
				shorter = body.Substring(0, at);
				peeled = true;
			}
			return peeled;
		}
	}
}
