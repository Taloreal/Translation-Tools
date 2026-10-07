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
		/// Every pointer line of a dialogue file, in file order. Header and blank lines are skipped.
		/// </summary>
		public static List<AlignmentLine> Read(string dialoguePath) {
			List<AlignmentLine> lines = new();
			foreach (string raw in NScripterSplit.ReadLines(dialoguePath)) {
				if (NScripterSplit.TryReadPointer(raw, out string pointer, out int index, out string rest) == true) {
					AlignmentLine line = new();
					line.Index = index;
					line.Text = rest;
					if (rest.StartsWith("【") == true) {
						int close = rest.IndexOf('】');
						if (close > 0) {
							line.HasNametag = true;
							line.Name = rest.Substring(1, close - 1).Trim().Trim('"').Trim();
						}
					}
					line.Bare = BareText(rest);
					lines.Add(line);
				}
			}
			return lines;
		}


		/// <summary>
		/// The words of a line and nothing else. The nametag goes. When the line uses
		/// double quotes (Siglus), only what sits inside them is kept, which drops the
		/// control words between. Otherwise (NScripter) the backtick and the click-wait
		/// characters go.
		/// </summary>
		public static string BareText(string rest) {
			string text = rest;
			if (text.StartsWith("【") == true) {
				int close = text.IndexOf('】');
				if (close > 0) {
					text = text.Substring(close + 1);
				}
			}
			StringBuilder bare = new();
			if (text.Contains('"') == true) {
				bool inside = false;
				foreach (char letter in text) {
					if (letter == '"') {
						inside = inside == false;
					}
					if (letter != '"' && inside == true) {
						bare.Append(letter);
					}
				}
			}
			if (text.Contains('"') == false) {
				foreach (char letter in text) {
					if (letter != '`' && letter != '\\' && letter != '@') {
						bare.Append(letter);
					}
				}
			}
			return bare.ToString().Trim();
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
		/// A short form of a line's text for a row or a prompt.
		/// </summary>
		public static string Preview(string text, int most) {
			string shown = text.Replace("\r", " ").Replace("\n", " ");
			if (shown.Length > most) {
				shown = shown.Substring(0, most) + "...";
			}
			return shown;
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
