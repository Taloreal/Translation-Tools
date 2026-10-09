// File: AlignmentLine.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// One dialogue line as the walk sees it: its index, whether it carries a nametag and
	/// which, the text after the pointer as written, and the bare text used for the
	/// automatic match. Engine-agnostic: both splits write "::index::" then the line.
	/// </summary>
	public class AlignmentLine {

		/// <summary>The index inside the pointer.</summary>
		public int Index = 0;

		/// <summary>Whether the line opens with a 【nametag】.</summary>
		public bool HasNametag = false;

		/// <summary>The name inside the brackets, without the quotes Siglus puts around it. Empty for narration.</summary>
		public string Name = "";

		/// <summary>Everything after the pointer, as written.</summary>
		public string Text = "";

		/// <summary>The words alone: no nametag, no structural quotes, no engine control characters.</summary>
		public string Bare = "";

		/// <summary>Standalone kind only: this line IS a speaker's name, naming the line after it; it has no words of its own.</summary>
		public bool IsTagLine = false;

		/// <summary>Standalone kind only: the index of the tag line that names this line, or -1 when none does.</summary>
		public int TagIndex = -1;
	}


	/// <summary>
	/// Reads dialogue files for the walk and judges whether two lines are the same text.
	/// </summary>
	public static class AlignmentLines {

		/// <summary>Two lines this long or longer may match automatically.</summary>
		public const int AutoMatchLeastLength = 15;

		/// <summary>How alike two bare texts must be, over the longer one's length, to match automatically.</summary>
		public const double AutoMatchSimilarity = 0.93;


		/// <summary>
		/// The dialogue file for a key inside a checkpoint's split.
		/// </summary>
		public static string DialoguePath(Checkpoint checkpoint, string key) {
			return Path.Combine(CheckpointInspector.FolderOf(checkpoint.Path), CheckpointInspector.SplitFolder, NScripterSplit.DialoguesFolder, key + ".txt");
		}


		/// <summary>
		/// Every dialogue key of a checkpoint's split that holds text, in no particular
		/// order; empty when it has no split. Only the top of dialogues\ is read: the files
		/// the NScripter split puts under dialogues\empty\ hold no dialogue, and nothing in
		/// them can be paired, learned or counted.
		/// </summary>
		public static List<string> DialogueKeys(Checkpoint checkpoint) {
			List<string> keys = new();
			string dialogues = Path.Combine(CheckpointInspector.FolderOf(checkpoint.Path), CheckpointInspector.SplitFolder, NScripterSplit.DialoguesFolder);
			if (Directory.Exists(dialogues) == true) {
				foreach (string file in Directory.GetFiles(dialogues, "*.txt", SearchOption.TopDirectoryOnly)) {
					keys.Add(Path.GetFileNameWithoutExtension(file));
				}
			}
			return keys;
		}


		/// <summary>
		/// Every pointer line of a dialogue file, in file order. Header and blank lines are skipped.
		/// </summary>
		public static List<AlignmentLine> Read(string dialoguePath) {
			return Read(dialoguePath, NametagConvention.Tgd());
		}


		/// <summary>
		/// Every pointer line of a dialogue file, in file order, with speakers read by a
		/// convention. Null means the file is not known to tag speakers: no line gets one.
		/// </summary>
		/// <param name="dialoguePath">The dialogue file.</param>
		/// <param name="convention">How this file tags its speaker, or null for none known.</param>
		public static List<AlignmentLine> Read(string dialoguePath, NametagConvention? convention) {
			List<AlignmentLine> lines = new();
			AlignmentLine? pendingTag = null;
			foreach (string raw in NScripterSplit.ReadLines(dialoguePath)) {
				if (NScripterSplit.TryReadPointer(raw, out string pointer, out int index, out string rest) == true) {
					AlignmentLine line = new();
					line.Index = index;
					line.Text = rest;
					bool standalone = convention != null && convention.Standalone == true;
					if (standalone == false && convention != null && convention.Matches(rest, out string name) == true) {
						line.HasNametag = true;
						line.Name = name;
					}
					if (standalone == true && convention!.Matches(rest, out string lineName) == true) {
						// The standalone kind: this line is the name, and the next line is the
						// one it names. The tag line has no words; the named line carries the
						// speaker and remembers which line gave it.
						line.IsTagLine = true;
						line.Name = lineName;
						pendingTag = line;
					}
					if (standalone == true && line.IsTagLine == false && pendingTag != null) {
						line.HasNametag = true;
						line.Name = pendingTag.Name;
						line.TagIndex = pendingTag.Index;
						pendingTag = null;
					}
					line.Bare = BareText(rest, convention);
					if (line.IsTagLine == true) {
						line.Bare = "";
					}
					lines.Add(line);
				}
			}
			return lines;
		}


		/// <summary>
		/// The text after the pointer of every pointer line, for learning the file's
		/// speaker-tag convention before the lines are read.
		/// </summary>
		public static List<string> ReadTexts(string dialoguePath) {
			List<string> texts = new();
			foreach (string raw in NScripterSplit.ReadLines(dialoguePath)) {
				if (NScripterSplit.TryReadPointer(raw, out string pointer, out int index, out string rest) == true) {
					texts.Add(rest);
				}
			}
			return texts;
		}


		/// <summary>
		/// The words of a line and nothing else. The nametag goes. When the line uses
		/// double quotes (Siglus), only what sits inside them is kept, which drops the
		/// control words between. Otherwise (NScripter) the backtick and the click-wait
		/// characters go.
		/// </summary>
		public static string BareText(string rest) {
			return BareText(rest, NametagConvention.Tgd());
		}


		/// <summary>
		/// The words of a line and nothing else, with the speaker tag read by a convention.
		/// </summary>
		/// <param name="rest">The text after the pointer.</param>
		/// <param name="convention">How the file tags its speaker, or null for none known.</param>
		public static string BareText(string rest, NametagConvention? convention) {
			string text = rest;
			if (convention != null && convention.Matches(text, out string name) == true) {
				int close = text.IndexOf(convention.Closer, 1);
				if (close > 0) {
					text = text.Substring(close + 1);
				}
			}
			text = CutComment(text);
			StringBuilder bare = new();
			if (text.Contains('"') == true) {
				// Quoted (Siglus): keep what sits inside the quotes. A backslash-escaped quote
				// is a letter, not a boundary. Leaving one quoted part for the next means a
				// control word (a line break) stood between, so a space stands in for it.
				bool inside = false;
				int at = 0;
				while (at < text.Length) {
					char letter = text[at];
					bool escapedQuote = inside == true && letter == '\\' && at + 1 < text.Length && text[at + 1] == '"';
					if (escapedQuote == true) {
						bare.Append('"');
						at++;
					}
					if (escapedQuote == false && letter == '"') {
						if (inside == true) {
							bare.Append(' ');
						}
						inside = inside == false;
					}
					if (escapedQuote == false && letter != '"' && inside == true) {
						bare.Append(letter);
					}
					at++;
				}
			}
			if (text.Contains('"') == false) {
				// Unquoted: an NScripter line, or a Siglus line the repair left bare. Standing-alone
				// control words go, then the click-wait marks.
				text = text.TrimEnd();
				// A Siglus control word may be glued to the sentence ("出た。r"). It is only taken
				// off when what precedes it is not an ASCII letter, so an English word ending in r
				// is left alone.
				bool trimmed = true;
				while (trimmed == true) {
					trimmed = false;
					foreach (string control in new string[] { "nl", "r" }) {
						if (trimmed == false && text.EndsWith(control, StringComparison.Ordinal) == true) {
							int cut = text.Length - control.Length;
							bool glued = cut > 0 && char.IsAsciiLetter(text[cut - 1]) == true;
							if (glued == false) {
								text = text.Substring(0, cut).TrimEnd();
								trimmed = true;
							}
						}
					}
				}
				foreach (string word in text.Split(' ')) {
					bool controlWord = word == "r" || word == "nl";
					if (controlWord == false) {
						foreach (char letter in word) {
							if (letter != '`' && letter != '\\' && letter != '@') {
								bare.Append(letter);
							}
						}
						bare.Append(' ');
					}
				}
			}
			string joined = bare.ToString();
			while (joined.Contains("  ") == true) {
				joined = joined.Replace("  ", " ");
			}
			return joined.Trim();
		}


		/// <summary>
		/// Cuts a trailing "//" comment that sits outside the quotes. One inside a quoted
		/// string is text and stays.
		/// </summary>
		private static string CutComment(string text) {
			string kept = text;
			bool inside = false;
			int at = 0;
			bool cut = false;
			while (cut == false && at < text.Length) {
				char letter = text[at];
				bool escapedQuote = inside == true && letter == '\\' && at + 1 < text.Length && text[at + 1] == '"';
				if (escapedQuote == true) {
					at++;
				}
				if (escapedQuote == false && letter == '"') {
					inside = inside == false;
				}
				if (escapedQuote == false && inside == false && letter == '/' && at + 1 < text.Length && text[at + 1] == '/') {
					kept = text.Substring(0, at);
					cut = true;
				}
				at++;
			}
			return kept;
		}


		/// <summary>
		/// Whether two lines are the same text, near enough: both long enough, and alike
		/// above the threshold.
		/// </summary>
		public static bool AutoMatch(AlignmentLine first, AlignmentLine second) {
			bool match = false;
			if (first.Bare.Length >= AutoMatchLeastLength && second.Bare.Length >= AutoMatchLeastLength) {
				match = Similarity(first.Bare, second.Bare) > AutoMatchSimilarity;
			}
			return match;
		}


		/// <summary>
		/// How alike two texts are: one minus the edit distance over the longer length, so
		/// identical texts score 1 and texts with nothing in common score near 0.
		/// </summary>
		public static double Similarity(string first, string second) {
			double similarity = 1.0;
			int longer = Math.Max(first.Length, second.Length);
			if (longer > 0) {
				similarity = 1.0 - ((double) EditDistance(first, second) / longer);
			}
			return similarity;
		}


		/// <summary>
		/// A short form of a line's text for a row or a prompt, cut by console columns: a
		/// Japanese or other wide character takes two, so a cut line never wraps.
		/// </summary>
		/// <param name="text">The text.</param>
		/// <param name="columns">The most columns it may take, the "..." included.</param>
		public static string Preview(string text, int columns) {
			string flat = text.Replace("\r", " ").Replace("\n", " ");
			string shown = flat;
			if (Columns(flat) > columns) {
				StringBuilder cut = new();
				int used = 0;
				int room = Math.Max(0, columns - 3);
				foreach (char letter in flat) {
					int width = ColumnsOf(letter);
					if (used + width <= room) {
						cut.Append(letter);
						used += width;
					}
				}
				shown = cut.ToString() + "...";
			}
			return shown;
		}


		/// <summary>
		/// How many console columns a text takes.
		/// </summary>
		public static int Columns(string text) {
			int columns = 0;
			foreach (char letter in text) {
				columns += ColumnsOf(letter);
			}
			return columns;
		}


		/// <summary>
		/// Two for a wide character (CJK, kana, full-width forms), one for anything else.
		/// </summary>
		private static int ColumnsOf(char letter) {
			int columns = 1;
			bool wide = (letter >= 'ᄀ' && letter <= 'ᅟ')
				|| (letter >= '⺀' && letter <= '꓏')
				|| (letter >= '가' && letter <= '힣')
				|| (letter >= '豈' && letter <= '﫿')
				|| (letter >= '︰' && letter <= '﹏')
				|| (letter >= '＀' && letter <= '｠')
				|| (letter >= '￠' && letter <= '￦');
			if (wide == true) {
				columns = 2;
			}
			return columns;
		}


		/// <summary>
		/// The Levenshtein distance: how many single-character edits turn one text into the other.
		/// </summary>
		private static int EditDistance(string first, string second) {
			int[] previous = new int[second.Length + 1];
			int[] current = new int[second.Length + 1];
			for (int column = 0; column <= second.Length; column++) {
				previous[column] = column;
			}
			for (int row = 1; row <= first.Length; row++) {
				current[0] = row;
				for (int column = 1; column <= second.Length; column++) {
					int cost = 1;
					if (first[row - 1] == second[column - 1]) {
						cost = 0;
					}
					int best = Math.Min(previous[column] + 1, current[column - 1] + 1);
					current[column] = Math.Min(best, previous[column - 1] + cost);
				}
				int[] swap = previous;
				previous = current;
				current = swap;
			}
			return previous[second.Length];
		}
	}
}
