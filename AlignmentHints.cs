// File: AlignmentHints.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// What the model recommends at one point of the walk, after reading a window of lines
	/// from both sides.
	/// </summary>
	public class AlignmentHint {

		/// <summary>The windows pair line for line as far as shown.</summary>
		public const string Aligned = "ALIGNED";

		/// <summary>An editable-side line has no partner.</summary>
		public const string MissingEdit = "MISSING A";

		/// <summary>A reference-side line has no partner.</summary>
		public const string MissingRef = "MISSING B";

		/// <summary>An editable-side line's partner is further down the reference window.</summary>
		public const string MovedEdit = "MOVED A";

		/// <summary>A reference-side line's partner is further down the editable window.</summary>
		public const string MovedRef = "MOVED B";

		/// <summary>One of the words above, or empty when no recommendation came.</summary>
		public string Call = "";

		/// <summary>The window position (1 = the current line) the call is about; 0 for Aligned.</summary>
		public int At = 0;

		/// <summary>For a Moved call: the position on the other side the line matches.</summary>
		public int Partner = 0;

		/// <summary>The model's reason, one line.</summary>
		public string Reason = "";

		/// <summary>Why no recommendation came, when Call is empty.</summary>
		public string Problem = "";
	}


	/// <summary>
	/// Asks the language model where two windows of dialogue first diverge and what the
	/// divergence is. A recommendation, never a decision: the walk shows it beside the
	/// three answers and the user picks. Answers are cached per position; after two failed
	/// round trips the model is left alone for the rest of the session.
	/// </summary>
	public static class AlignmentHints {

		private static readonly Dictionary<string, AlignmentHint> Cache = new();

		private static int failures = 0;

		/// <summary>True once the model has failed twice and is no longer asked.</summary>
		public static bool GaveUp {
			get { return failures >= 2; }
		}


		/// <summary>
		/// Forgets cached answers and failures, for a new walk.
		/// </summary>
		public static void Clear() {
			Cache.Clear();
			failures = 0;
		}


		/// <summary>
		/// The recommendation for the walk's current point: a window of undecided lines from
		/// each side, starting at the current one, the length of the context-after setting.
		/// </summary>
		/// <param name="edit">The editable side's lines.</param>
		/// <param name="editAt">Position of the current editable line.</param>
		/// <param name="reference">The reference side's lines.</param>
		/// <param name="refAt">Position of the current reference line.</param>
		/// <param name="pairing">The record, so decided lines are left out of the windows.</param>
		/// <param name="editLabel">The editable checkpoint's label, for the prompt.</param>
		/// <param name="refLabel">The reference checkpoint's label, for the prompt.</param>
		public static AlignmentHint Recommend(List<AlignmentLine> edit, int editAt, List<AlignmentLine> reference, int refAt,
			AlignmentPairing pairing, string editLabel, string refLabel) {
			string key = editAt + ":" + refAt;
			if (Cache.ContainsKey(key) == false) {
				AlignmentHint hint = new();
				if (GaveUp == true) {
					hint.Problem = "the model is not being asked any more this session";
				}
				if (GaveUp == false) {
					int window = Math.Max(1, LlmClient.ContextAfter);
					List<AlignmentLine> windowA = Window(edit, editAt, window, pairing, true);
					List<AlignmentLine> windowB = Window(reference, refAt, window, pairing, false);
					string reply = LlmClient.Complete(SystemText(), UserText(windowA, windowB, editLabel, refLabel), out string error, 400, 120);
					if (error.Length > 0) {
						failures++;
						hint.Problem = error;
					}
					if (error.Length == 0) {
						Parse(reply, hint);
					}
				}
				Cache[key] = hint;
			}
			return Cache[key];
		}


		/// <summary>
		/// The next undecided lines on one side from a position, up to a count.
		/// </summary>
		public static List<AlignmentLine> Window(List<AlignmentLine> lines, int from, int count, AlignmentPairing pairing, bool editSide) {
			List<AlignmentLine> window = new();
			int at = from;
			while (at < lines.Count && window.Count < count) {
				bool decided = pairing.EditDecided(lines[at].Index);
				if (editSide == false) {
					decided = pairing.RefDecided(lines[at].Index);
				}
				if (decided == false) {
					window.Add(lines[at]);
				}
				at++;
			}
			return window;
		}


		private static string SystemText() {
			return "You are aligning two versions of the same visual-novel script, line by line. "
				+ "Side A and side B may be in different languages: a line and its translation are the SAME line. "
				+ "Only a different moment in the story, or a line one side simply does not have, is a divergence. "
				+ "Both windows start at the two lines being compared and run on in script order.\n"
				+ "Reply with ONE of these on the first line, then one short sentence of reason on the second:\n"
				+ "ALIGNED - every line shown pairs with the line in the same position on the other side.\n"
				+ "MISSING A k - side A's line k has no partner on side B (B goes straight from A's line k-1 to A's line k+1).\n"
				+ "MISSING B k - side B's line k has no partner on side A.\n"
				+ "MOVED A k n - side A's line k is the same line as side B's line n, with n greater than k.\n"
				+ "MOVED B k n - side B's line k is the same line as side A's line n, with n greater than k.\n"
				+ "Use the smallest k where the sides stop pairing up. Lines are numbered from 1 within each window.";
		}


		private static string UserText(List<AlignmentLine> windowA, List<AlignmentLine> windowB, string editLabel, string refLabel) {
			StringBuilder text = new();
			text.Append("Side A (").Append(editLabel).Append("):\n");
			AppendWindow(text, windowA);
			text.Append("\nSide B (").Append(refLabel).Append("):\n");
			AppendWindow(text, windowB);
			return text.ToString();
		}


		private static void AppendWindow(StringBuilder text, List<AlignmentLine> window) {
			int position = 1;
			foreach (AlignmentLine line in window) {
				string speaker = "(narration)";
				if (line.HasNametag == true) {
					speaker = "【" + line.Name + "】";
				}
				text.Append(position).Append(". ").Append(speaker).Append(' ').Append(line.Bare).Append('\n');
				position++;
			}
			if (window.Count == 0) {
				text.Append("(no lines left on this side)\n");
			}
		}


		/// <summary>
		/// Reads the model's first line into a call, position and partner, and its second
		/// line into the reason. Anything unreadable becomes a problem, not a guess.
		/// </summary>
		private static void Parse(string reply, AlignmentHint hint) {
			string[] lines = reply.Replace("\r", "").Trim().Split('\n');
			string first = lines[0].Trim().ToUpperInvariant().Replace("*", "").Replace(":", " ");
			string[] words = first.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length > 1) {
				hint.Reason = AlignmentLines.Preview(lines[1].Trim(), 300);
			}
			if (words.Length >= 1 && words[0] == "ALIGNED") {
				hint.Call = AlignmentHint.Aligned;
			}
			if (words.Length >= 3 && (words[0] == "MISSING" || words[0] == "MOVED") && (words[1] == "A" || words[1] == "B")) {
				int.TryParse(words[2], out hint.At);
				if (words[0] == "MISSING" && hint.At > 0) {
					hint.Call = AlignmentHint.MissingRef;
					if (words[1] == "A") {
						hint.Call = AlignmentHint.MissingEdit;
					}
				}
				if (words[0] == "MOVED" && words.Length >= 4 && hint.At > 0) {
					int.TryParse(words[3], out hint.Partner);
					if (hint.Partner > 0) {
						hint.Call = AlignmentHint.MovedRef;
						if (words[1] == "A") {
							hint.Call = AlignmentHint.MovedEdit;
						}
					}
				}
			}
			if (hint.Call.Length == 0) {
				hint.Problem = "the model's answer was not understood: " + AlignmentLines.Preview(reply.Trim(), 200);
			}
		}
	}
}
