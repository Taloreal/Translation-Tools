// File: NametagConvention.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// How a dialogue file marks its speaker, learned from the file itself rather than
	/// assumed. Many lines open with a punctuation pair, 【Mai】 or [Mai] or (Mai), and what
	/// sits inside repeats heavily, because a cast is small and speech is not. A pair whose
	/// enclosed text repeats like that is the file's nametag; one whose enclosed text is
	/// always different, 「speech」, is not. The model is then asked once whether the rule
	/// reads right, and its answer is shown, not obeyed.
	/// </summary>
	public class NametagConvention {

		/// <summary>The fewest lines that must open with the pair for it to count, whichever way it is then proved a speaker tag.</summary>
		public const int LeastTaggedLines = 5;

		/// <summary>The fewest different glossary characters the glossary path must see among its hits: one name five times could be a word, two names are a cast.</summary>
		public const int LeastKnownSpeakers = 2;

		/// <summary>Without a glossary, the fewest tagged lines the repetition proof needs: twice the glossary proof's.</summary>
		public const int LeastRepetitionLines = 10;

		/// <summary>Without a glossary, the fewest different names that must each appear at least twice: twice the glossary proof's speakers.</summary>
		public const int LeastRepeatingSpeakers = 4;

		/// <summary>The stored word for the standalone kind: the tag is a whole line holding only the name, above the line it names.</summary>
		public const string LineKind = "line";

		/// <summary>
		/// What conform appends to a standalone tag line in the dialogue file so a translator
		/// sees it is a name: an engine comment, so it is harmless should it ever leak into
		/// a script, and stripped by every reader and by the join before text goes back.
		/// </summary>
		public const string LineMarker = " ;<NAMETAG>";

		/// <summary>
		/// True for the standalone kind: no opener and closer; a line whose whole text is one
		/// of the glossary's names (Cast) is a tag line, naming the line after it.
		/// </summary>
		public bool Standalone = false;

		/// <summary>For the standalone kind: every name the glossary knows, any character, any form.</summary>
		public List<string> Cast = new();

		/// <summary>The character the tag opens with.</summary>
		public char Opener = ' ';

		/// <summary>The character the tag closes with.</summary>
		public char Closer = ' ';

		/// <summary>The names seen, most frequent first.</summary>
		public List<string> Names = new();

		/// <summary>How many lines open with the tag.</summary>
		public int TaggedLines = 0;

		/// <summary>How many lines the file has.</summary>
		public int TotalLines = 0;

		/// <summary>How many tagged lines name a character the checkpoint's glossary already knows. The strongest sign the pair is a nametag.</summary>
		public int GlossaryHits = 0;

		/// <summary>What the model said when asked whether the rule reads right: "agreed", "disagreed", or "" when not asked or unreachable.</summary>
		public string ModelVerdict = "";

		/// <summary>The model's reason, one line.</summary>
		public string ModelReason = "";


		/// <summary>
		/// The standalone kind with a cast: names are the glossary's, read when the
		/// convention is made for a checkpoint.
		/// </summary>
		public static NametagConvention Line(List<CharacterEntry> characters) {
			NametagConvention convention = new();
			convention.Standalone = true;
			convention.ModelVerdict = "stored";
			foreach (CharacterEntry entry in characters) {
				foreach (string name in entry.Names) {
					if (CharacterEntry.Contains(convention.Cast, name) == false) {
						convention.Cast.Add(name);
					}
				}
			}
			return convention;
		}


		/// <summary>
		/// A dialogue line's text without the standalone marker, if it carries one.
		/// </summary>
		public static string StripMarker(string text) {
			string stripped = text;
			string trimmed = text.TrimEnd();
			if (trimmed.EndsWith(LineMarker.Trim(), StringComparison.Ordinal) == true) {
				stripped = trimmed.Substring(0, trimmed.Length - LineMarker.Trim().Length).TrimEnd();
			}
			return stripped;
		}


		/// <summary>
		/// The convention TGD uses, taken as read under the TGD switch.
		/// </summary>
		public static NametagConvention Tgd() {
			NametagConvention convention = new();
			convention.Opener = '【';
			convention.Closer = '】';
			convention.ModelVerdict = "TGD only";
			return convention;
		}


		/// <summary>
		/// The convention a checkpoint's files are read with: TGD's under the TGD switch,
		/// otherwise the one stored on the checkpoint, or null when none was learned.
		/// </summary>
		public static NametagConvention? For(Checkpoint checkpoint) {
			NametagConvention? convention = null;
			if (TgdFeatures.Enabled == true) {
				convention = Tgd();
			}
			if (TgdFeatures.Enabled == false) {
				string folder = CheckpointInspector.FolderOf(checkpoint.Path);
				CheckpointInfo info = CheckpointInfo.Load(folder);
				convention = FromStored(info.SpeakerTag);
				if (convention != null && convention.Standalone == true) {
					// The standalone kind's names are the glossary's: read them now, once.
					convention = Line(Glossary.Characters(folder));
				}
			}
			return convention;
		}


		/// <summary>
		/// The convention a checkpoint stored: its two tag characters, opener then closer.
		/// Null when nothing is stored.
		/// </summary>
		public static NametagConvention? FromStored(string stored) {
			NametagConvention? convention = null;
			if (string.Equals(stored, LineKind, StringComparison.Ordinal) == true) {
				convention = new NametagConvention();
				convention.Standalone = true;
				convention.ModelVerdict = "stored";
			}
			if (convention == null && stored.Length == 2) {
				convention = new NametagConvention();
				convention.Opener = stored[0];
				convention.Closer = stored[1];
				convention.ModelVerdict = "stored";
			}
			return convention;
		}


		/// <summary>The two tag characters as the checkpoint stores them, or the standalone kind's word.</summary>
		public string Stored {
			get {
				string stored = Opener.ToString() + Closer.ToString();
				if (Standalone == true) {
					stored = LineKind;
				}
				return stored;
			}
		}


		/// <summary>
		/// Learns the convention from a file's line texts, or returns null when no
		/// punctuation pair opens enough lines with repeating content.
		/// </summary>
		/// <param name="texts">Each line's text after its pointer.</param>
		public static NametagConvention? Learn(List<string> texts) {
			return Learn(texts, new List<CharacterEntry>());
		}


		/// <summary>
		/// Learns the convention with the checkpoint's character glossary as a second witness:
		/// a pair is the nametag once five lines hold exactly a known character's name, in
		/// either language or an alias, between its marks; unknown names may sit among them.
		/// </summary>
		/// <param name="texts">Each line's text after its pointer.</param>
		/// <param name="characters">The checkpoint's glossary characters; may be empty.</param>
		public static NametagConvention? Learn(List<string> texts, List<CharacterEntry> characters) {
			NametagConvention? best = null;
			Dictionary<char, int> openers = new();
			foreach (string text in texts) {
				string trimmed = text.TrimStart();
				if (trimmed.Length > 0 && IsPunctuationLike(trimmed[0]) == true) {
					if (openers.ContainsKey(trimmed[0]) == false) {
						openers[trimmed[0]] = 0;
					}
					openers[trimmed[0]]++;
				}
			}
			// Every pair seen is tried, even on one line; the repetition and glossary tests decide.
			int least = 1;
			foreach (KeyValuePair<char, int> candidate in openers) {
				if (candidate.Value >= least) {
					NametagConvention? found = TryPair(texts, candidate.Key, characters);
					if (found != null && (best == null || found.GlossaryHits > best.GlossaryHits || (found.GlossaryHits == best.GlossaryHits && found.TaggedLines > best.TaggedLines))) {
						best = found;
					}
				}
			}
			if (best != null) {
				best.TotalLines = texts.Count;
			}
			return best;
		}


		/// <summary>
		/// Asks the model once whether the learned rule reads right, and records its answer.
		/// Nothing changes on a no: the rule stands and the doubt is shown.
		/// </summary>
		/// <param name="label">The checkpoint's label, for the prompt and the screen.</param>
		public void ConfirmWithModel(string label) {
			StringBuilder examples = new();
			int shown = 0;
			foreach (string name in Names) {
				if (shown < 8) {
					if (shown > 0) {
						examples.Append(", ");
					}
					examples.Append(Opener).Append(name).Append(Closer);
					shown++;
				}
			}
			string system = "You are checking a rule a tool inferred about a visual-novel script. Reply YES or NO on the first line, then one short sentence of reason.";
			string user = "In the script \"" + label + "\", " + TaggedLines + " of " + TotalLines + " lines open with text inside " + Opener + " and " + Closer
				+ ", and that text repeats: " + Names.Count + " distinct values, for example " + examples + ". "
				+ "The tool reads " + Opener + "..." + Closer + " at the start of a line as the SPEAKER'S NAME TAG, and a line without it as narration. Is that right?";
			Console.WriteLine("Asking the model whether " + Opener + "Name" + Closer + " is the speaker tag in " + label + " ...");
			string reply = LlmClient.Complete(system, user, out string error, 0, 0);
			ModelVerdict = "";
			ModelReason = "";
			if (error.Length > 0) {
				ModelReason = error;
			}
			if (error.Length == 0) {
				string[] lines = reply.Replace("\r", "").Trim().Split('\n');
				string first = lines[0].Trim().ToUpperInvariant();
				if (first.StartsWith("YES") == true) {
					ModelVerdict = "agreed";
				}
				if (first.StartsWith("NO") == true) {
					ModelVerdict = "disagreed";
				}
				if (lines.Length > 1) {
					ModelReason = lines[1].Trim();
				}
				if (ModelVerdict.Length == 0) {
					ModelReason = "answer not understood: " + AlignmentLines.Preview(reply.Trim(), 120);
				}
			}
		}


		/// <summary>
		/// Whether a line's text opens with the tag, and the name inside it.
		/// </summary>
		public bool Matches(string text, out string name) {
			name = "";
			bool matches = false;
			string trimmed = text.TrimStart();
			if (Standalone == true) {
				// The whole line is the tag when it is one of the cast's names, marker or no marker.
				string bare = StripMarker(trimmed).Trim();
				if (bare.Length > 0 && CharacterEntry.Contains(Cast, bare) == true) {
					name = bare;
					matches = true;
				}
			}
			if (Standalone == false && trimmed.Length > 0 && trimmed[0] == Opener) {
				int close = trimmed.IndexOf(Closer, 1);
				if (close > 0) {
					name = trimmed.Substring(1, close - 1).Trim().Trim('"').Trim();
					matches = name.Length > 0;
				}
			}
			return matches;
		}


		/// <summary>
		/// One line for the screen: the rule, how many lines it covers, and what the model said.
		/// </summary>
		public string Describe() {
			string text = Opener + "Name" + Closer + " on " + TaggedLines + " of " + TotalLines + " lines, " + Names.Count + " names";
			if (GlossaryHits > 0) {
				text += ", " + GlossaryHits + " in the glossary";
			}
			if (ModelVerdict == "agreed") {
				text += ", model agreed";
			}
			if (ModelVerdict == "disagreed") {
				text += ", MODEL DISAGREED: " + ModelReason;
			}
			if (ModelVerdict == "TGD only") {
				text = Opener + "Name" + Closer + " (TGD only)";
			}
			if (ModelVerdict == "stored") {
				text = Opener + "Name" + Closer + " (learned earlier)";
			}
			if (Standalone == true) {
				text = "a name on its own line, above the line it names (" + Cast.Count + " names from the glossary)";
			}
			if (ModelVerdict.Length == 0 && ModelReason.Length > 0) {
				text += ", model not asked (" + ModelReason + ")";
			}
			return text;
		}


		/// <summary>
		/// Tries one opener: finds its usual closer and tests whether the enclosed text
		/// repeats enough to be a cast rather than speech.
		/// </summary>
		private static NametagConvention? TryPair(List<string> texts, char opener, List<CharacterEntry> characters) {
			NametagConvention? convention = null;
			Dictionary<char, int> closers = new();
			foreach (string text in texts) {
				string trimmed = text.TrimStart();
				if (trimmed.Length > 1 && trimmed[0] == opener) {
					int at = 1;
					bool found = false;
					while (found == false && at < trimmed.Length) {
						if (IsPunctuationLike(trimmed[at]) == true && trimmed[at] != opener && trimmed[at] != '"') {
							if (closers.ContainsKey(trimmed[at]) == false) {
								closers[trimmed[at]] = 0;
							}
							closers[trimmed[at]]++;
							found = true;
						}
						at++;
					}
				}
			}
			// Every closer seen is tried, not only the commonest: a name that holds
			// punctuation itself, "???", puts its own marks ahead of the real closer, and
			// the right one is whichever tags the most lines with names that repeat.
			foreach (KeyValuePair<char, int> candidate in closers) {
				// Any closer seen is tried; the floor is on the lines it tags, below.
				if (candidate.Value > 0) {
					Dictionary<string, int> names = new();
					int tagged = 0;
					NametagConvention trial = new();
					trial.Opener = opener;
					trial.Closer = candidate.Key;
					foreach (string text in texts) {
						if (trial.Matches(text, out string name) == true) {
							tagged++;
							if (names.ContainsKey(name) == false) {
								names[name] = 0;
							}
							names[name]++;
						}
					}
					int hits = 0;
					int knownSpeakers = 0;
					foreach (KeyValuePair<string, int> entry in names) {
						if (InGlossary(entry.Key, characters) == true) {
							hits += entry.Value;
							knownSpeakers++;
						}
					}
					// A pair is a nametag when at least five lines hold exactly a glossary name between
					// its marks, from at least two different characters - names the glossary has not
					// met yet may sit among them - or, with no glossary to help, on twice the evidence:
					// ten tagged lines and four different names that each appear at least twice. Among pairs that
					// pass, more glossary hits win, then more tagged lines.
					int repeating = 0;
					foreach (KeyValuePair<string, int> entry in names) {
						if (entry.Value >= 2) {
							repeating++;
						}
					}
					bool repeats = tagged >= LeastRepetitionLines && repeating >= LeastRepeatingSpeakers;
					bool known = hits >= LeastTaggedLines && knownSpeakers >= LeastKnownSpeakers;
					bool better = convention == null || hits > convention.GlossaryHits || (hits == convention.GlossaryHits && tagged > convention.TaggedLines);
					if ((repeats == true || known == true) && better == true) {
						trial.GlossaryHits = hits;
						List<KeyValuePair<string, int>> ordered = new(names);
						ordered.Sort((first, second) => second.Value.CompareTo(first.Value));
						foreach (KeyValuePair<string, int> entry in ordered) {
							trial.Names.Add(entry.Key);
						}
						trial.TaggedLines = tagged;
						convention = trial;
					}
				}
			}
			return convention;
		}


		/// <summary>
		/// Whether a name is any of a glossary character's names, ignoring case.
		/// </summary>
		private static bool InGlossary(string name, List<CharacterEntry> characters) {
			return Glossary.Find(characters, name) != null;
		}


		/// <summary>
		/// Brackets, quotes and other marks: anything that is not a letter, digit or space.
		/// </summary>
		private static bool IsPunctuationLike(char letter) {
			return char.IsLetterOrDigit(letter) == false && char.IsWhiteSpace(letter) == false;
		}
	}
}
