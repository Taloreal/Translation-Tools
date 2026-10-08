// File: AlignmentHints.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// What the model recommends at one point of the walk, after reading a window of lines
	/// from both sides.
	/// </summary>
	public class AlignmentHint {

		/// <summary>The two current lines are the same line.</summary>
		public const string Same = "SAME";

		/// <summary>The current editable-side line has no partner.</summary>
		public const string MissingEdit = "MISSING A";

		/// <summary>The current reference-side line has no partner.</summary>
		public const string MissingRef = "MISSING B";

		/// <summary>The current editable-side line's partner is further down the reference window.</summary>
		public const string MovedEdit = "MOVED A";

		/// <summary>The current reference-side line's partner is further down the editable window.</summary>
		public const string MovedRef = "MOVED B";

		/// <summary>One of the words above, or empty when no recommendation came.</summary>
		public string Call = "";

		/// <summary>For a Moved call: the 1-based position in the other side's window that the current line matches.</summary>
		public int Partner = 0;

		/// <summary>The model's reason, one line.</summary>
		public string Reason = "";

		/// <summary>Why no recommendation came, when Call is empty.</summary>
		public string Problem = "";

		/// <summary>How long the model took, in milliseconds; 0 for a cached or unasked answer.</summary>
		public long Milliseconds = 0;
	}


	/// <summary>
	/// Asks the language model about the ONE pair in front of the user - the current line on
	/// each side - with the lines after them as context. A recommendation, never a decision:
	/// the walk shows it beside the three answers and the user rules on every line. Answers are cached per position; after two failed
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
		/// Forgets the cached answer for one position, so the next Recommend asks again.
		/// Also forgives earlier failures, since the user is asking for another try.
		/// </summary>
		public static void Forget(int editAt, int refAt) {
			Cache.Remove(editAt + ":" + refAt);
			failures = 0;
		}


		/// <summary>
		/// The recommendation for the current pair. The model sees a window of undecided lines
		/// from each side, starting at the current one, the length of the context-after setting,
		/// and answers about the first line of each.
		/// </summary>
		/// <param name="edit">The editable side's lines.</param>
		/// <param name="editAt">Position of the current editable line.</param>
		/// <param name="reference">The reference side's lines.</param>
		/// <param name="refAt">Position of the current reference line.</param>
		/// <param name="pairing">The record, so decided lines are left out of the windows.</param>
		/// <param name="editLabel">The editable checkpoint's label, for the prompt.</param>
		/// <param name="refLabel">The reference checkpoint's label, for the prompt.</param>
		public static AlignmentHint Recommend(List<AlignmentLine> edit, int editAt, List<AlignmentLine> reference, int refAt,
			AlignmentPairing pairing, string editLabel, string refLabel, string evidence) {
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
					// No cap of its own: the answer is two lines, but a model that reasons first needs the
					// room the Settings allow, or it returns nothing.
					Console.WriteLine("Asking the model about " + windowA.Count + " lines of " + editLabel + " and " + windowB.Count + " of " + refLabel + " ... (one question at a time; a reasoning model can take a while)");
					System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
					string reply = LlmClient.Complete(SystemText(), UserText(windowA, windowB, editLabel, refLabel, evidence), out string error, 0, 0);
					clock.Stop();
					hint.Milliseconds = clock.ElapsedMilliseconds;
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
				+ "You are asked about ONE pair only: line 1 of side A against line 1 of side B. "
				+ "The lines after them are context in script order, to help you judge.\n"
				+ "Reply with ONE of these on the first line, then one short sentence of reason on the second:\n"
				+ "SAME - A's line 1 and B's line 1 are the same line.\n"
				+ "MISSING A - A's line 1 has no partner on side B (B's line 1 belongs with a later A line).\n"
				+ "MISSING B - B's line 1 has no partner on side A.\n"
				+ "MOVED A n - A's line 1 is the same line as B's line n, with n greater than 1.\n"
				+ "MOVED B n - B's line 1 is the same line as A's line n, with n greater than 1.\n"
				+ "\n"
				+ "Rule: a line may be REPLACED in one version - same speaker, same place, different words, often the same voice clip. "
				+ "When the tool reports the speaker order matching on both sides around the pair, or the same audio clip, a line whose "
				+ "words are not a translation is still the SAME line; answer SAME.\n"
				+ "Rule: MOVED is for a crossing only - the lines between the current line and its partner pair up across the two sides. "
				+ "When those in-between lines simply have no partner on the other side, one version has extra lines there: answer "
				+ "MISSING for the side whose current line has no partner, one line at a time, not MOVED.\n"
				+ "Rule: when exactly one of the two lines carries a speaker tag and the other is narration, they are almost never "
				+ "the same line. Look for the line's partner a few lines down the other side and answer MOVED or MISSING, not SAME.\n"
				+ "\n"
				+ "Worked example. One version lists seven blows in a single line, \"Strike, charge, slash, crush...\"; "
				+ "the other gives each blow its own line, \"Charging...\", \"Impacting...\", \"Striking...\" and so on, "
				+ "then both continue with the same next sentence. When the single line faces \"Charging...\" the answer is SAME: "
				+ "the first fragment is where the translation of that line begins. At every following fragment the answer is "
				+ "MISSING for the fragment's side, because the line now facing it on the other side is a later sentence, "
				+ "whose partner comes after the fragments end. Count what each side actually has; never pair a line with one "
				+ "that merely sits in the same position.\n"
				+ "\n"
				+ "Second worked example, a swap. Side A reads: 1. (narration) \"She says it like she sees right through me, so I dig in my heels.\" "
				+ "2. [Ryuuga] \"N-Natsuki has nothing to do with this!\" Side B reads: 1. [Ryuuga] \"な、なつきは関係ない。\" "
				+ "2. (narration) \"舞が見透かしたような言い方をするので、俺は意地を張って言い返す。\" Nothing is missing: the two versions "
				+ "put the same two lines in the opposite order. The answer is MOVED A 2, because A's line 1 is B's line 2. "
				+ "Before answering MISSING, look for the line's partner a few lines down the other side; a speaker on one side "
				+ "facing narration on the other, with the same speaker one line away, is a swap, not a gap."
				+ "\n\nThird worked example, a replaced line. Side A: 1. [Mai] \"Nice trousers...\" 2. [Ryuuga] \"I'd like you to leave.\" "
				+ "Side B: 1. [Mai] \"ぬふふふ……\" 2. [Ryuuga] \"……制服に着替えるんで、出て行ってもらいたいのだが\" "
				+ "The tool reports the same audio clip on both line 1s and the speaker order Mai, Ryuuga, Mai matching on both sides. "
				+ "Line 1 is not a translation of a giggle, but it is the same line: the translator replaced it. The answer is SAME.";
		}


		private static string UserText(List<AlignmentLine> windowA, List<AlignmentLine> windowB, string editLabel, string refLabel, string evidence) {
			StringBuilder text = new();
			if (evidence.Length > 0) {
				text.Append("Evidence the tool found: ").Append(evidence).Append("\n\n");
			}
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
			if (words.Length >= 1 && words[0] == "SAME") {
				hint.Call = AlignmentHint.Same;
			}
			if (words.Length >= 2 && words[0] == "MISSING" && (words[1] == "A" || words[1] == "B")) {
				hint.Call = AlignmentHint.MissingRef;
				if (words[1] == "A") {
					hint.Call = AlignmentHint.MissingEdit;
				}
			}
			if (words.Length >= 3 && words[0] == "MOVED" && (words[1] == "A" || words[1] == "B")) {
				int.TryParse(words[2], out hint.Partner);
				if (hint.Partner > 1) {
					hint.Call = AlignmentHint.MovedRef;
					if (words[1] == "A") {
						hint.Call = AlignmentHint.MovedEdit;
					}
				}
			}
			if (hint.Call.Length == 0) {
				hint.Problem = "the model's answer was not understood: " + AlignmentLines.Preview(reply.Trim(), 200);
			}
		}
	}
}
