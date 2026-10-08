// File: AlignmentWalk.cs
// Namespace: TranslationTools
using System.Text;

using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The walk: one dialogue file of a pair, the editable side against the reference, line
	/// against line. Lines whose text matches closely pair on their own; under a debugger
	/// the TGD nametag anchor pairs lines whose speakers agree; everything else is a
	/// question with three answers - same line, one side has a line the other lacks here,
	/// or the partner is elsewhere - with the model's recommendation on that one pair beside
	/// them. The user rules on every line; the model only advises. Every answer is written
	/// at once, so a reopened pair resumes where it stopped.
	/// </summary>
	public static class AlignmentWalk {

		/// <summary>Rows the question screen must leave for everything that is not context: the menu, the hint, the headers, the two current lines and a margin.</summary>
		private const int FixedRows = 18;

		/// <summary>Whether both files of the current walk tag their speakers, so a tag on one line and none on the other is evidence.</summary>
		private static bool tagsKnown = false;

		/// <summary>The editable checkpoint's glossary characters for the current walk, for judging whether two names are one speaker.</summary>
		private static List<CharacterEntry> cast = new();

		/// <summary>The editable checkpoint's folder for the current walk, where a learned speaker is written.</summary>
		private static string editFolder = "";

		/// <summary>The editable checkpoint of the current walk, whose files a learned speaker conforms.</summary>
		private static Checkpoint? editCheckpoint = null;

		/// <summary>The reference checkpoint's folder for the current walk, whose glossary is kept identical to the editable's.</summary>
		private static string refFolder = "";

		/// <summary>How many paired lines have shown each pair of differing tags, keyed "edit tag|reference tag". Other lines between do not reset it.</summary>
		private static readonly Dictionary<string, int> pendingPairs = new();

		/// <summary>The tag pairs the user declined to learn this walk, so they are not asked again.</summary>
		private static readonly HashSet<string> declinedPairs = new();


		/// <summary>
		/// After two lines were paired, by whatever witness. The walk walks: a differing
		/// spelling goes to the gated alias learning, and nothing is repaired here. Speaker
		/// tags that disagree are the speaker repair's business, once the alignment is done.
		/// </summary>
		private static void AfterPairing(AlignmentLine a, AlignmentLine b) {
			NoteSpeakerMismatch(a, b);
		}


		/// <summary>
		/// After two lines were paired, by whatever witness: when their speaker tags differ,
		/// counts that pair of names, and at the set count asks the model whether the two tags
		/// are one name in two languages. A yes, or no model to ask, offers to learn them as
		/// one character in the editable checkpoint's glossary. The count is per pair of names
		/// and survives other lines between; a declined pair is left alone for the walk.
		/// </summary>
		private static void NoteSpeakerMismatch(AlignmentLine a, AlignmentLine b) {
			bool candidate = tagsKnown == true && a.HasNametag == true && b.HasNametag == true && SameSpeaker(a, b, cast) == false;
			if (candidate == true) {
				string key = a.Name + "|" + b.Name;
				if (declinedPairs.Contains(key) == false) {
					if (pendingPairs.ContainsKey(key) == false) {
						pendingPairs[key] = 0;
					}
					pendingPairs[key]++;
					if (pendingPairs[key] >= AlignmentSettings.LearnSpeakerLines) {
						int count = pendingPairs[key];
						pendingPairs.Remove(key);
						string verdict = SameNameVerdict(a.Name, b.Name, out string reason);
						if (verdict == "no") {
							declinedPairs.Add(key);
						}
						if (verdict != "no") {
							bool learned = OfferToLearn(a.Name, b.Name, verdict, reason, count);
							if (learned == false) {
								declinedPairs.Add(key);
							}
						}
					}
				}
			}
		}


		/// <summary>
		/// Asks the model whether two speaker tags are the same name in different languages.
		/// </summary>
		/// <param name="reason">The model's reason, or why it was not asked.</param>
		/// <returns>"yes", "no", or "unasked" when no model answered.</returns>
		private static string SameNameVerdict(string first, string second, out string reason) {
			string verdict = "unasked";
			reason = "the model is not being asked this session";
			if (AlignmentHints.GaveUp == false) {
				Console.WriteLine("Asking the model whether " + first + " and " + second + " are one name ...");
				string reply = LlmClient.Complete(
					"You are given two speaker name tags from two versions of the same visual-novel script, which may be in different languages. "
					+ "Reply YES if they are the same character's name - a translation, transliteration or romanization of each other - or NO if they are different characters. "
					+ "One word on the first line, then one short sentence of reason.",
					"Tag on side A: " + first + "\nTag on side B: " + second, out string error, 0, 0);
				reason = error;
				if (error.Length == 0) {
					string[] lines = reply.Replace("\r", "").Trim().Split('\n');
					string first_ = lines[0].Trim().ToUpperInvariant();
					if (first_.StartsWith("YES") == true) {
						verdict = "yes";
					}
					if (first_.StartsWith("NO") == true) {
						verdict = "no";
					}
					if (lines.Length > 1) {
						reason = lines[1].Trim();
					}
					if (verdict == "unasked") {
						reason = "the model's answer was not understood: " + AlignmentLines.Preview(reply.Trim(), 120);
					}
				}
			}
			return verdict;
		}


		/// <summary>
		/// Offers to record two tags as one character: onto the glossary entry that already
		/// carries either name, or as a new entry whose written name is the editable side's
		/// tag. A name joining an existing character is offered as its written name, and the
		/// editable checkpoint's files are conformed to whatever was decided.
		/// </summary>
		private static bool OfferToLearn(string editName, string refName, string verdict, string reason, int count) {
			string modelLine = "The model says they are one name: " + reason;
			if (verdict == "unasked") {
				modelLine = "The model could not be asked (" + reason + ").";
			}
			bool learn = YesNoMenu.Ask("Learn " + refName + " = " + editName + " as one character in the glossary?",
				count + " paired line(s) had " + editName + " on one side and " + refName + " on the other.\n" + modelLine, verdict == "yes");
			if (learn == true && editCheckpoint != null) {
				CharacterEntry? entry = Glossary.Find(cast, editName);
				if (entry == null) {
					entry = Glossary.Find(cast, refName);
				}
				string replaces = "";
				List<string> added = new();
				if (entry != null) {
					replaces = entry.Written;
					if (entry.Add(editName) == true) {
						added.Add(editName);
					}
					if (entry.Add(refName) == true) {
						added.Add(refName);
					}
				}
				if (entry == null) {
					entry = new CharacterEntry();
					entry.Written = editName;
					entry.Add(editName);
					entry.Add(refName);
				}
				bool chosen = false;
				if (entry.Provisional == true) {
					// VNDB's name was a placeholder; the script's own tag is the name.
					Console.WriteLine(entry.Written + " was provisional, from VNDB; " + editName + " becomes the written name.");
					entry.Written = editName;
					entry.Provisional = false;
					chosen = true;
				}
				foreach (string name in added) {
					if (chosen == false) {
						bool make = YesNoMenu.Ask("Make " + name + " the name " + editCheckpoint.Label + " writes for " + entry.Written + "?",
							"Every speaker tag of this character in " + editCheckpoint.Label + " would be rewritten to it.");
						if (make == true) {
							entry.Written = name;
							chosen = true;
						}
					}
				}
				string problem = NametagConform.SaveAndConform(editCheckpoint, entry, replaces, out string report);
				if (problem.Length > 0) {
					Console.WriteLine(problem);
					ConsoleExt.WaitForEnter("continue");
				}
				if (problem.Length == 0) {
					// The two sides share one glossary: the same entry goes to the reference,
					// without touching its files.
					string mirrored = Glossary.SaveCharacter(refFolder, entry, replaces);
					if (mirrored.Length > 0) {
						Console.WriteLine("Not mirrored to the reference's glossary: " + mirrored);
					}
					cast = Glossary.Characters(editFolder);
					Console.WriteLine("Learned: " + entry.NamesText + "; the translation writes " + entry.Written + ". " + report + " From here the walk treats them as one speaker.");
					ConsoleExt.WaitForEnter("continue");
				}
			}
			return learn;
		}




		/// <summary>
		/// How far the speaker order matches on both sides around a pair, within the speaker
		/// window: the lines before, counted back until a speaker differs, and the lines
		/// after, counted forward the same way. Matching order with differing words is the
		/// mark of a line replaced in one version, so the result is told to the model as
		/// evidence and shown on the screen. Empty when tags are unknown or the window is 0.
		/// </summary>
		/// <param name="evidence">The sentence for the model; empty when there is nothing to say.</param>
		/// <returns>The sentence for the screen; empty when there is nothing to say.</returns>
		private static string SpeakerOrderNote(List<AlignmentLine> editLines, int e, List<AlignmentLine> refLines, int r, out string evidence) {
			string note = "";
			evidence = "";
			int window = AlignmentSettings.SpeakerWindow;
			if (tagsKnown == true && window > 0 && SameSpeaker(editLines[e], refLines[r], cast) == true) {
				MatchedRun(editLines, e, refLines, r, window, null, out int before, out int after, out int sameClips, out bool contradiction);
				if (before + after > 0) {
					string speaker = "narration";
					if (editLines[e].HasNametag == true) {
						speaker = editLines[e].Name;
					}
					note = "Speakers: " + speaker + " on both sides, and the speaker order matches for " + before + " line(s) before and " + after + " after. If the words differ, this is probably a replaced line.";
					evidence = "both current lines are spoken by " + speaker + ", and the speaker order on the two sides matches for " + before
						+ " line(s) before and " + after + " line(s) after this pair (window " + window + "). A line in such a run whose words differ was most likely replaced in one version: the same line.";
				}
			}
			return note;
		}


		/// <summary>
		/// The speaker-tag convention for one checkpoint: TGD's under the switch, otherwise
		/// the one stored in its checkpoint.info by the Alignment menu's learning step. Null
		/// when neither applies.
		/// </summary>
		private static NametagConvention? TagsFor(Checkpoint checkpoint) {
			return NametagConvention.For(checkpoint);
		}


		private static string DescribeTags(NametagConvention? convention) {
			string text = "none learned; every line counts as untagged";
			if (convention != null) {
				text = convention.Describe();
			}
			return text;
		}

		/// <summary>How much of a line a preview may show: the window's width less the index column.</summary>
		private static int PreviewWidth {
			get { return Math.Max(20, WindowWidth() - 24); }
		}


		/// <summary>
		/// Walks a pair's file from the first undecided line on each side to the end.
		/// </summary>
		/// <param name="pair">The pair.</param>
		/// <param name="edit">The editable checkpoint.</param>
		/// <param name="reference">The reference checkpoint.</param>
		public static void Run(AlignmentPair pair, Checkpoint edit, Checkpoint reference) {
			string editPath = AlignmentLines.DialoguePath(edit, pair.Key);
			string refPath = AlignmentLines.DialoguePath(reference, pair.RefKey);
			string problem = "";
			if (File.Exists(editPath) == false) {
				problem = "Missing: " + editPath;
			}
			if (problem.Length == 0 && File.Exists(refPath) == false) {
				problem = "Missing: " + refPath;
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				ConsoleExt.WaitForEnter("continue");
			}
			if (problem.Length == 0) {
				// A side has speakers only when its checkpoint was given a speaker-tag rule,
				// learned from a file the user pointed at (Alignment menu), or under the TGD
				// switch. Nothing is learned here: without a rule every line is untagged.
				NametagConvention? editTags = TagsFor(edit);
				NametagConvention? refTags = TagsFor(reference);
				tagsKnown = editTags != null && refTags != null;
				Console.WriteLine("Speaker tags on " + edit.Label + ": " + DescribeTags(editTags));
				Console.WriteLine("Speaker tags on " + reference.Label + ": " + DescribeTags(refTags));
				List<AlignmentLine> editLines = AlignmentLines.Read(editPath, editTags);
				List<AlignmentLine> refLines = AlignmentLines.Read(refPath, refTags);
				AlignmentPairing pairing = AlignmentPairing.Load(pair.Folder);
				// One glossary on both sides: a tag resolves the same way wherever it sits, and
				// a correction writes the shared written name. The caller made them the same.
				List<CharacterEntry> characters = Glossary.Characters(CheckpointInspector.FolderOf(edit.Path));
				cast = characters;
				editFolder = CheckpointInspector.FolderOf(edit.Path);
				refFolder = CheckpointInspector.FolderOf(reference.Path);
				editCheckpoint = edit;
				pendingPairs.Clear();
				declinedPairs.Clear();
				VoiceEvidence voice = new(edit, reference, pair.Key, pair.RefKey);
				AlignmentHints.Clear();
				Walk(pairing, editLines, refLines, characters, voice, edit.Label, reference.Label);
			}
		}


		/// <summary>
		/// The loop. Stops when both sides are decided (complete) or the user stops.
		/// </summary>
		private static void Walk(AlignmentPairing pairing, List<AlignmentLine> editLines, List<AlignmentLine> refLines,
			List<CharacterEntry> characters, VoiceEvidence voice, string editLabel, string refLabel) {
			bool anchors = TgdFeatures.Enabled;
			bool stop = false;
			int autoRun = 0;
			int anchorRun = 0;
			int voiceRun = 0;
			int structureRun = 0;
			while (stop == false) {
				int e = NextUndecided(editLines, pairing, true, 0);
				int r = NextUndecided(refLines, pairing, false, 0);
				if (e >= editLines.Count || r >= refLines.Count) {
					FinishTail(pairing, editLines, refLines, e, r);
					pairing.MarkComplete();
					Console.WriteLine(RunNote(autoRun, anchorRun, voiceRun, structureRun));
					Console.WriteLine("Complete: " + pairing.PairedCount + " pairs, " + pairing.EditOnlyCount + " only on " + editLabel
						+ ", " + pairing.RefOnlyCount + " only on " + refLabel + ".");
					ConsoleExt.WaitForEnter("continue");
					stop = true;
				}
				if (stop == false) {
					AlignmentLine a = editLines[e];
					AlignmentLine b = refLines[r];
					string verdict = voice.Verdict(a.Index, b.Index, out string audio);
					bool decided = false;
					if (verdict == VoiceEvidence.SameClip) {
						// Two witnesses from different directions: the audio says one clip, the
						// model says one line. Both agreeing is enough to pair without asking.
						SpeakerOrderNote(editLines, e, refLines, r, out string orderEvidence);
						AlignmentHint hint = AlignmentHints.Recommend(editLines, e, refLines, r, pairing, editLabel, refLabel, "the same audio clip plays on both lines" + Joined(orderEvidence));
						if (hint.Call == AlignmentHint.Same) {
							pairing.Add(Pair(a.Index, b.Index, "voice+model"));
							Progress(a, b, "same clip, model agrees");
							voiceRun++;
							decided = true;
							AfterPairing(a, b);
						}
					}
					int structureRunLength = Math.Max(AlignmentSettings.AutoPairRun, AlignmentSettings.ModelPairRun);
					if (decided == false && verdict != VoiceEvidence.Different && tagsKnown == true && structureRunLength > 0
						&& SameSpeaker(a, b, cast) == true) {
						// One audio rule, two thresholds. The speaker order is measured once over
						// the longer window, with the clips that agree inside it: a long enough run
						// pairs on structure alone; a shorter one asks the model as the third
						// witness. The current line's own clip never matters, so an unvoiced line
						// inside a voiced stretch is covered by its neighbours.
						MatchedRun(editLines, e, refLines, r, structureRunLength, voice, out int before, out int after, out int sameClips, out bool contradiction);
						int run = before + after + 1;
						bool audioAgrees = sameClips >= AlignmentSettings.AutoPairClips && contradiction == false;
						if (audioAgrees == true && AlignmentSettings.AutoPairRun > 0 && run >= AlignmentSettings.AutoPairRun) {
							pairing.Add(Pair(a.Index, b.Index, "order+audio"));
							Progress(a, b, "speaker order + audio over " + run + " lines");
							structureRun++;
							decided = true;
							AfterPairing(a, b);
						}
						if (decided == false && audioAgrees == true && AlignmentSettings.ModelPairRun > 0 && run >= AlignmentSettings.ModelPairRun) {
							SpeakerOrderNote(editLines, e, refLines, r, out string orderEvidence);
							AlignmentHint hint = AlignmentHints.Recommend(editLines, e, refLines, r, pairing, editLabel, refLabel,
								"the audio agrees on " + sameClips + " clip(s) in the matching run" + Joined(orderEvidence));
							if (hint.Call == AlignmentHint.Same) {
								pairing.Add(Pair(a.Index, b.Index, "order+audio+model"));
								Progress(a, b, "speaker order + audio over " + run + " lines, model agrees");
								voiceRun++;
								decided = true;
								AfterPairing(a, b);
							}
						}
					}
					if (decided == false && verdict != VoiceEvidence.Different && AlignmentLines.AutoMatch(a, b) == true) {
						pairing.Add(Pair(a.Index, b.Index, "auto"));
						Progress(a, b, "same text");
						autoRun++;
						decided = true;
						AfterPairing(a, b);
					}
					if (decided == false && verdict != VoiceEvidence.Different && tagsKnown == true && AlignmentSettings.SpeakerWindow > 0
						&& a.Bare.Length > 0 && a.Bare == b.Bare && SameSpeaker(a, b, cast) == true) {
						// A short identical line is too common to trust on its own text, so it
						// pairs only when the speaker agrees and the speaker order around it does
						// too, for the set number of lines within the window.
						MatchedRun(editLines, e, refLines, r, AlignmentSettings.SpeakerWindow, null, out int before, out int after, out int ignoredClips, out bool ignoredContradiction);
						if (before + after >= AlignmentSettings.ShortLineNeighbours) {
							pairing.Add(Pair(a.Index, b.Index, "auto"));
							Progress(a, b, "identical short line, speakers around it agree");
							autoRun++;
							decided = true;
						}
					}
					if (decided == false && verdict != VoiceEvidence.Different && anchors == true && SameSpeaker(a, b, characters) == true) {
						pairing.Add(Pair(a.Index, b.Index, "nametag"));
						Progress(a, b, "TGD nametag");
						anchorRun++;
						decided = true;
					}
					if (decided == false) {
						string note = RunNote(autoRun, anchorRun, voiceRun, structureRun);
						autoRun = 0;
						anchorRun = 0;
						voiceRun = 0;
						structureRun = 0;
						stop = Ask(pairing, editLines, e, refLines, r, editLabel, refLabel, note, verdict, audio);
					}
				}
			}
		}


		/// <summary>
		/// One question at a divergence. Shows both sides around the current lines, the
		/// model's recommendation, and the answers.
		/// </summary>
		/// <returns>True when the user chose to stop.</returns>
		private static bool Ask(AlignmentPairing pairing, List<AlignmentLine> editLines, int e, List<AlignmentLine> refLines, int r,
			string editLabel, string refLabel, string note, string verdict, string audio) {
			bool stop = false;
			AlignmentLine a = editLines[e];
			AlignmentLine b = refLines[r];
			string orderNote = SpeakerOrderNote(editLines, e, refLines, r, out string orderEvidence);
			string evidence = orderEvidence;
			if (verdict == VoiceEvidence.SameClip) {
				evidence = "the same audio clip plays on both lines" + Joined(orderEvidence);
			}
			if (verdict == VoiceEvidence.Different) {
				evidence = "the audio says these are NOT the same line (" + audio + ")" + Joined(orderEvidence);
			}
			AlignmentHint hint = AlignmentHints.Recommend(editLines, e, refLines, r, pairing, editLabel, refLabel, evidence);
			CorrectMovedToMissing(hint, editLines, e, refLines, r, pairing);
			bool ruledOut = verdict == VoiceEvidence.Different;
			bool runOffered = false;
			if (runOffered == false) {
				StringBuilder text = new();
				if (note.Length > 0) {
					text.Append(note).Append('\n');
				}
				if (audio.Length > 0) {
					text.Append(audio);
					if (ruledOut == true) {
						text.Append(" These are not the same line; \"Same line\" is not offered.");
					}
					text.Append('\n');
				}
				if (orderNote.Length > 0) {
					text.Append(orderNote).Append('\n');
				}
				// Both sides must fit the window with the menu, or the redraw garbles. Each side
				// gets half of what is left after the fixed rows, before taking a third of it.
				int perSide = Math.Max(2, (WindowHeight() - FixedRows) / 2);
				int before = Math.Min(LlmClient.ContextBefore, perSide / 3);
				int after = Math.Min(LlmClient.ContextAfter, perSide - before);
				text.Append(ContextText(editLines, e, pairing, true, editLabel, before, after));
				text.Append('\n');
				text.Append(ContextText(refLines, r, pairing, false, refLabel, before, after));
				string hintLine = HintText(hint);
				if (ruledOut == true && hint.Call == AlignmentHint.Same) {
					hintLine += "   <- contradicted by the audio";
				}
				if (ruledOut == false && hint.Call == AlignmentHint.Same && tagsKnown == true && a.HasNametag != b.HasNametag) {
					hintLine += "   <- doubtful: one line names a speaker, the other does not";
				}
				text.Append('\n').Append(hintLine).Append('\n');
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText(text.ToString());
				// The rows are listed by name so "Same line" can be left out when the audio has
				// ruled it out without shifting what the other rows mean.
				List<string> actions = new();
				if (ruledOut == false) {
					menu.AddChoice(new ConsoleMenuItem("Same line" + Mark(hint, AlignmentHint.Same)));
					actions.Add("same");
				}
				menu.AddChoice(new ConsoleMenuItem(editLabel + " has this line, " + refLabel + " does not" + Mark(hint, AlignmentHint.MissingEdit)));
				actions.Add("edit-only");
				menu.AddChoice(new ConsoleMenuItem(refLabel + " has this line, " + editLabel + " does not" + Mark(hint, AlignmentHint.MissingRef)));
				actions.Add("ref-only");
				menu.AddChoice(new ConsoleMenuItem("Out of order: one line's partner is further on..." + MovedMark(hint)));
				actions.Add("moved");
				// An adjacent swap the model recommends is two known pairs; one row takes both.
				AlignmentLine? swapThis = null;
				AlignmentLine? swapThat = null;
				if (hint.Partner == 2 && (hint.Call == AlignmentHint.MovedEdit || hint.Call == AlignmentHint.MovedRef)) {
					List<AlignmentLine> nextEdit = AlignmentHints.Window(editLines, e + 1, 1, pairing, true);
					List<AlignmentLine> nextRef = AlignmentHints.Window(refLines, r + 1, 1, pairing, false);
					if (nextEdit.Count == 1 && nextRef.Count == 1) {
						bool crossAgrees = tagsKnown == false || SameSpeaker(b, nextEdit[0], cast) == true;
						if (hint.Call == AlignmentHint.MovedRef) {
							crossAgrees = tagsKnown == false || SameSpeaker(a, nextRef[0], cast) == true;
						}
						if (crossAgrees == true) {
							swapThis = nextEdit[0];
							swapThat = nextRef[0];
						}
					}
				}
				if (swapThis != null && swapThat != null) {
					menu.AddChoice(new ConsoleMenuItem("Accept the model's swap: " + editLabel + " " + a.Index + " = " + refLabel + " " + swapThat.Index
						+ ", and " + refLabel + " " + b.Index + " = " + editLabel + " " + swapThis.Index));
					actions.Add("swap");
				}
				menu.AddChoice(new ConsoleMenuItem("Ask the model again"));
				actions.Add("retry");
				menu.AddChoice(new ConsoleMenuItem("Undo the previous decision"));
				actions.Add("undo");
				menu.AddChoice(new ConsoleMenuItem("Stop here (resume later)"));
				actions.Add("stop");
				int choice = menu.GetChoice();
				string action = "stop";
				if (choice >= 0 && choice < actions.Count) {
					action = actions[choice];
				}
				if (action == "same") {
					pairing.Add(Pair(a.Index, b.Index, "user"));
					AfterPairing(a, b);
				}
				if (action == "edit-only") {
					pairing.Add(Pair(a.Index, -1, PairingEntry.Only));
				}
				if (action == "ref-only") {
					pairing.Add(Pair(-1, b.Index, PairingEntry.Only));
				}
				if (action == "swap" && swapThis != null && swapThat != null) {
					pairing.Add(Pair(a.Index, swapThat.Index, "model"));
					pairing.Add(Pair(swapThis.Index, b.Index, "model"));
					AfterPairing(a, swapThat);
					AfterPairing(swapThis, b);
				}
				if (action == "moved") {
					OutOfOrder(pairing, editLines, e, refLines, r, editLabel, refLabel, hint);
				}
				if (action == "retry") {
					// Nothing is decided; the loop comes back to this position and asks afresh.
					AlignmentHints.Forget(e, r);
				}
				if (action == "undo") {
					PairingEntry? undone = pairing.UndoLast();
					if (undone == null) {
						Console.WriteLine("Nothing to undo.");
						ConsoleExt.WaitForEnter("continue");
					}
				}
				if (action == "stop") {
					stop = true;
				}
			}
			return stop;
		}


		/// <summary>
		/// The third answer: which side holds the out-of-order line, then its partner from
		/// the other side's undecided lines from here on.
		/// </summary>
		private static void OutOfOrder(AlignmentPairing pairing, List<AlignmentLine> editLines, int e, List<AlignmentLine> refLines, int r,
			string editLabel, string refLabel, AlignmentHint hint) {
			ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
			menu.SetPreChoiceText("Which side's line is the one whose partner lies further on?\n"
				+ "  " + editLabel + " " + editLines[e].Index + ": " + AlignmentLines.Preview(editLines[e].Bare, PreviewWidth) + "\n"
				+ "  " + refLabel + " " + refLines[r].Index + ": " + AlignmentLines.Preview(refLines[r].Bare, PreviewWidth) + "\n" + HintText(hint));
			menu.AddChoice(new ConsoleMenuItem(editLabel + "'s line; find its partner on " + refLabel + Mark(hint, AlignmentHint.MovedEdit)));
			menu.AddChoice(new ConsoleMenuItem(refLabel + "'s line; find its partner on " + editLabel + Mark(hint, AlignmentHint.MovedRef)));
			menu.AddChoice(new ConsoleMenuItem("Cancel"));
			int side = menu.GetChoice();
			if (side == 0) {
				int partner = PickPartner(refLines, r, pairing, false, refLabel, hint.Call == AlignmentHint.MovedEdit ? hint.Partner : 0);
				if (partner >= 0) {
					pairing.Add(Pair(editLines[e].Index, partner, "user"));
				}
			}
			if (side == 1) {
				int partner = PickPartner(editLines, e, pairing, true, editLabel, hint.Call == AlignmentHint.MovedRef ? hint.Partner : 0);
				if (partner >= 0) {
					pairing.Add(Pair(partner, refLines[r].Index, "user"));
				}
			}
		}


		/// <summary>
		/// The catalogue of one side's undecided lines from a position on, paged. The model's
		/// suggested position, when it gave one, is marked.
		/// </summary>
		/// <returns>The chosen line's index, or -1 for Back.</returns>
		private static int PickPartner(List<AlignmentLine> lines, int from, AlignmentPairing pairing, bool editSide, string label, int suggested) {
			List<AlignmentLine> candidates = AlignmentHints.Window(lines, from, lines.Count, pairing, editSide);
			List<string> rows = new();
			int position = 1;
			foreach (AlignmentLine line in candidates) {
				string mark = "";
				if (position == suggested) {
					mark = "  <- model";
				}
				rows.Add(line.Index.ToString().PadLeft(5) + "  " + Speaker(line) + " " + AlignmentLines.Preview(line.Bare, PreviewWidth) + mark);
				position++;
			}
			int picked = PagedPicker.Pick(rows, "Its partner on " + label);
			int index = -1;
			if (picked >= 0) {
				index = candidates[picked].Index;
			}
			return index;
		}


		/// <summary>
		/// Once one side is exhausted, every undecided line left on the other has no partner.
		/// </summary>
		private static void FinishTail(AlignmentPairing pairing, List<AlignmentLine> editLines, List<AlignmentLine> refLines, int e, int r) {
			while (e < editLines.Count) {
				if (pairing.EditDecided(editLines[e].Index) == false) {
					pairing.Add(Pair(editLines[e].Index, -1, PairingEntry.Only));
				}
				e++;
			}
			while (r < refLines.Count) {
				if (pairing.RefDecided(refLines[r].Index) == false) {
					pairing.Add(Pair(-1, refLines[r].Index, PairingEntry.Only));
				}
				r++;
			}
		}


		/// <summary>
		/// The speaker anchor: both narration, or both named with the same character through
		/// the glossary, where any of a character's names counts as that character.
		/// </summary>
		private static bool SameSpeaker(AlignmentLine a, AlignmentLine b, List<CharacterEntry> characters) {
			bool same = false;
			if (a.HasNametag == false && b.HasNametag == false) {
				same = true;
			}
			if (a.HasNametag == true && b.HasNametag == true) {
				same = string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
				foreach (CharacterEntry entry in characters) {
					if (same == false && entry.Has(a.Name) == true && entry.Has(b.Name) == true) {
						same = true;
					}
				}
			}
			return same;
		}


		/// <summary>
		/// The position of the first undecided line on a side, at or after a position.
		/// </summary>
		private static int NextUndecided(List<AlignmentLine> lines, AlignmentPairing pairing, bool editSide, int from) {
			int at = from;
			bool found = false;
			while (found == false && at < lines.Count) {
				bool decided = pairing.EditDecided(lines[at].Index);
				if (editSide == false) {
					decided = pairing.RefDecided(lines[at].Index);
				}
				if (decided == false) {
					found = true;
				}
				if (decided == true) {
					at++;
				}
			}
			return at;
		}


		/// <summary>
		/// One side around its current line: the context-before most recent decided lines,
		/// the current line marked, and the context-after undecided lines after it.
		/// </summary>
		private static string ContextText(List<AlignmentLine> lines, int at, AlignmentPairing pairing, bool editSide, string label, int beforeMost, int afterMost) {
			StringBuilder text = new();
			text.Append("-- ").Append(label).Append(" --\n");
			int before = 0;
			int back = at - 1;
			List<string> above = new();
			while (back >= 0 && before < beforeMost) {
				above.Insert(0, "      " + lines[back].Index.ToString().PadLeft(5) + "  " + Speaker(lines[back]) + " " + AlignmentLines.Preview(lines[back].Bare, PreviewWidth));
				before++;
				back--;
			}
			foreach (string line in above) {
				text.Append(line).Append('\n');
			}
			text.Append(">>>   ").Append(lines[at].Index.ToString().PadLeft(5)).Append("  ").Append(Speaker(lines[at])).Append(' ').Append(lines[at].Bare).Append('\n');
			List<AlignmentLine> after = AlignmentHints.Window(lines, at + 1, afterMost, pairing, editSide);
			foreach (AlignmentLine line in after) {
				text.Append("      ").Append(line.Index.ToString().PadLeft(5)).Append("  ").Append(Speaker(line)).Append(' ').Append(AlignmentLines.Preview(line.Bare, PreviewWidth)).Append('\n');
			}
			return text.ToString();
		}


		private static string HintText(AlignmentHint hint) {
			string took = "";
			if (hint.Milliseconds > 0) {
				took = " (" + (hint.Milliseconds / 1000.0).ToString("0.0") + " s)";
			}
			string text = "Model" + took + ": no recommendation";
			if (hint.Problem.Length > 0) {
				text += " (" + hint.Problem + ")";
			}
			if (hint.Call.Length > 0) {
				text = "Model" + took + ": " + hint.Call;
				if (hint.Partner > 0) {
					text += ", partner at line " + hint.Partner + " of the other side's window";
				}
				if (hint.Reason.Length > 0) {
					text += " - " + AlignmentLines.Preview(hint.Reason, PreviewWidth);
				}
			}
			return text;
		}


		/// <summary>A mark on the answer the model recommends for the current line.</summary>
		private static string Mark(AlignmentHint hint, string call) {
			string mark = "";
			if (hint.Call == call) {
				mark = "   <- model";
			}
			return mark;
		}


		private static string MovedMark(AlignmentHint hint) {
			string mark = "";
			if (hint.Call == AlignmentHint.MovedEdit || hint.Call == AlignmentHint.MovedRef) {
				mark = "   <- model";
			}
			return mark;
		}


		private static string RunNote(int autoRun, int anchorRun, int voiceRun, int structureRun) {
			string note = "";
			if (structureRun > 0) {
				note = structureRun + " line(s) matched on speaker order and audio";
			}
			if (voiceRun > 0) {
				if (note.Length > 0) {
					note += ", ";
				}
				note += voiceRun + " line(s) matched on audio and the model agreeing";
			}
			if (autoRun > 0) {
				if (note.Length > 0) {
					note += ", ";
				}
				note += autoRun + " line(s) matched on text";
			}
			if (anchorRun > 0) {
				if (note.Length > 0) {
					note += ", ";
				}
				note += anchorRun + " line(s) matched on nametag";
			}
			if (note.Length > 0) {
				note += " since the last question.";
			}
			return note;
		}


		/// <summary>The console's width, or a sensible one when it cannot be read.</summary>
		private static int WindowWidth() {
			int width = 120;
			try {
				width = Console.WindowWidth;
			}
			catch (IOException) {
				// No console window to measure; the fallback stands.
			}
			return width;
		}


		/// <summary>The console's height in rows, or a sensible one when it cannot be read.</summary>
		private static int WindowHeight() {
			int height = 40;
			try {
				height = Console.WindowHeight;
			}
			catch (IOException) {
				// No console window to measure; the fallback stands.
			}
			return height;
		}


		/// <summary>
		/// Counts how far the speaker order matches on both sides around a pair, back and
		/// forward, each stopping at the first speaker that differs or at the window. When
		/// voice evidence is given, every matched position is checked against the audio too:
		/// a pair the audio rules out is a contradiction, a pair on the same clip is counted.
		/// </summary>
		private static void MatchedRun(List<AlignmentLine> editLines, int e, List<AlignmentLine> refLines, int r, int window, VoiceEvidence? voice,
			out int before, out int after, out int sameClips, out bool contradiction) {
			before = 0;
			after = 0;
			sameClips = 0;
			contradiction = false;
			CheckAudio(voice, editLines[e].Index, refLines[r].Index, ref sameClips, ref contradiction);
			while (before < window && e - before - 1 >= 0 && r - before - 1 >= 0
				&& SameSpeaker(editLines[e - before - 1], refLines[r - before - 1], cast) == true) {
				before++;
				CheckAudio(voice, editLines[e - before].Index, refLines[r - before].Index, ref sameClips, ref contradiction);
			}
			while (after < window && e + after + 1 < editLines.Count && r + after + 1 < refLines.Count
				&& SameSpeaker(editLines[e + after + 1], refLines[r + after + 1], cast) == true) {
				after++;
				CheckAudio(voice, editLines[e + after].Index, refLines[r + after].Index, ref sameClips, ref contradiction);
			}
		}


		private static void CheckAudio(VoiceEvidence? voice, int editIndex, int refIndex, ref int sameClips, ref bool contradiction) {
			if (voice != null) {
				string verdict = voice.Verdict(editIndex, refIndex, out string ignored);
				if (verdict == VoiceEvidence.SameClip) {
					sameClips++;
				}
				if (verdict == VoiceEvidence.Different) {
					contradiction = true;
				}
			}
		}


		/// <summary>
		/// A structural check on a MOVED recommendation. MOVED means a crossing: the lines
		/// between the current line and its partner pair up with lines on this side. When none
		/// of those in-between lines could pair with any upcoming line here by speaker, nothing
		/// crosses; one version has extra lines, and the honest answer is MISSING for the side
		/// whose current line has no partner. The hint is rewritten to say so. Only runs when
		/// both sides are taught, since the test is the speaker.
		/// </summary>
		private static void CorrectMovedToMissing(AlignmentHint hint, List<AlignmentLine> editLines, int e, List<AlignmentLine> refLines, int r, AlignmentPairing pairing) {
			bool moved = hint.Call == AlignmentHint.MovedEdit || hint.Call == AlignmentHint.MovedRef;
			if (tagsKnown == true && moved == true && hint.Partner > 1) {
				// The side whose current line moves is "this"; the partner lies on "that" side.
				List<AlignmentLine> thisSide = editLines;
				int thisAt = e;
				bool thisIsEdit = true;
				List<AlignmentLine> thatSide = refLines;
				int thatAt = r;
				if (hint.Call == AlignmentHint.MovedRef) {
					thisSide = refLines;
					thisAt = r;
					thisIsEdit = false;
					thatSide = editLines;
					thatAt = e;
				}
				List<AlignmentLine> thatWindow = AlignmentHints.Window(thatSide, thatAt, hint.Partner, pairing, thisIsEdit == false);
				List<AlignmentLine> thisWindow = AlignmentHints.Window(thisSide, thisAt + 1, AlignmentSettings.SpeakerWindow + hint.Partner, pairing, thisIsEdit);
				bool anyCrosses = false;
				// Every line on that side before the partner, the current one included, must have
				// nothing it could pair with among the upcoming lines here.
				int between = 0;
				while (anyCrosses == false && between < hint.Partner - 1 && between < thatWindow.Count) {
					foreach (AlignmentLine upcoming in thisWindow) {
						if (anyCrosses == false && SameSpeaker(thatWindow[between], upcoming, cast) == true) {
							anyCrosses = true;
						}
					}
					between++;
				}
				if (anyCrosses == false && hint.Partner - 1 <= thatWindow.Count) {
					string was = hint.Call + " " + hint.Partner;
					hint.Call = AlignmentHint.MissingRef;
					if (thisIsEdit == false) {
						hint.Call = AlignmentHint.MissingEdit;
					}
					hint.Partner = 0;
					hint.Reason = "changed from " + was + ": the lines in between pair with nothing here by speaker, so they are extra lines, not a crossing. " + hint.Reason;
				}
			}
		}


		/// <summary>
		/// One line on the console for a decision the walk made on its own: which lines, why,
		/// and a glimpse of the text, so the user can follow the walk between questions.
		/// </summary>
		private static void Progress(AlignmentLine a, AlignmentLine b, string why) {
			Console.WriteLine("  " + a.Index.ToString().PadLeft(5) + " | " + b.Index.ToString().PadLeft(5) + "  " + why + "  " + Speaker(a) + " " + AlignmentLines.Preview(a.Bare, Math.Max(20, PreviewWidth / 2)));
		}


		/// <summary>A second piece of evidence joined onto a first with "; ", or nothing when it is empty.</summary>
		private static string Joined(string more) {
			string text = "";
			if (more.Length > 0) {
				text = "; " + more;
			}
			return text;
		}


		private static string Speaker(AlignmentLine line) {
			string speaker = "";
			if (line.HasNametag == true) {
				speaker = "【" + line.Name + "】";
			}
			return speaker;
		}


		private static PairingEntry Pair(int edit, int reference, string how) {
			PairingEntry entry = new();
			entry.Edit = edit;
			entry.Ref = reference;
			entry.How = how;
			return entry;
		}
	}
}
