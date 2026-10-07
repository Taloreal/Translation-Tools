// File: AlignmentWalk.cs
// Namespace: TranslationTools
using System.Diagnostics;
using System.Text;

using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The walk: one dialogue file of a pair, the editable side against the reference, line
	/// against line. Lines whose text matches closely pair on their own; under a debugger
	/// the TGD nametag anchor pairs lines whose speakers agree; everything else is a
	/// question with three answers - same line, one side has a line the other lacks here,
	/// or the partner is elsewhere - with the model's recommendation beside them. Every
	/// answer is written at once, so a reopened pair resumes where it stopped.
	/// </summary>
	public static class AlignmentWalk {

		private const int PreviewWidth = 70;


		/// <summary>
		/// Walks a pair's file from the first undecided line on each side to the end.
		/// </summary>
		/// <param name="pair">The pair.</param>
		/// <param name="edit">The editable checkpoint.</param>
		/// <param name="reference">The reference checkpoint.</param>
		public static void Run(AlignmentPair pair, Checkpoint edit, Checkpoint reference) {
			string editPath = AlignmentLines.DialoguePath(edit, pair.Key);
			string refPath = AlignmentLines.DialoguePath(reference, pair.Key);
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
				List<AlignmentLine> editLines = AlignmentLines.Read(editPath);
				List<AlignmentLine> refLines = AlignmentLines.Read(refPath);
				AlignmentPairing pairing = AlignmentPairing.Load(pair.Folder);
				List<CharacterEntry> characters = Glossary.Characters(CheckpointInspector.FolderOf(edit.Path));
				AlignmentHints.Clear();
				Walk(pairing, editLines, refLines, characters, edit.Label, reference.Label);
			}
		}


		/// <summary>
		/// The loop. Stops when both sides are decided (complete) or the user stops.
		/// </summary>
		private static void Walk(AlignmentPairing pairing, List<AlignmentLine> editLines, List<AlignmentLine> refLines,
			List<CharacterEntry> characters, string editLabel, string refLabel) {
			bool anchors = Debugger.IsAttached;
			bool stop = false;
			int autoRun = 0;
			int anchorRun = 0;
			while (stop == false) {
				int e = NextUndecided(editLines, pairing, true, 0);
				int r = NextUndecided(refLines, pairing, false, 0);
				if (e >= editLines.Count || r >= refLines.Count) {
					FinishTail(pairing, editLines, refLines, e, r);
					pairing.MarkComplete();
					Console.WriteLine(RunNote(autoRun, anchorRun));
					Console.WriteLine("Complete: " + pairing.PairedCount + " pairs, " + pairing.EditOnlyCount + " only on " + editLabel
						+ ", " + pairing.RefOnlyCount + " only on " + refLabel + ".");
					ConsoleExt.WaitForEnter("continue");
					stop = true;
				}
				if (stop == false) {
					AlignmentLine a = editLines[e];
					AlignmentLine b = refLines[r];
					bool decided = false;
					if (AlignmentLines.AutoMatch(a, b) == true) {
						pairing.Add(Pair(a.Index, b.Index, "auto"));
						autoRun++;
						decided = true;
					}
					if (decided == false && anchors == true && SameSpeaker(a, b, characters) == true) {
						pairing.Add(Pair(a.Index, b.Index, "nametag"));
						anchorRun++;
						decided = true;
					}
					if (decided == false) {
						string note = RunNote(autoRun, anchorRun);
						autoRun = 0;
						anchorRun = 0;
						stop = Ask(pairing, editLines, e, refLines, r, editLabel, refLabel, note);
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
			string editLabel, string refLabel, string note) {
			bool stop = false;
			AlignmentLine a = editLines[e];
			AlignmentLine b = refLines[r];
			AlignmentHint hint = AlignmentHints.Recommend(editLines, e, refLines, r, pairing, editLabel, refLabel);
			bool runOffered = OfferRun(pairing, editLines, e, refLines, r, hint, editLabel, refLabel);
			if (runOffered == false) {
				StringBuilder text = new();
				if (note.Length > 0) {
					text.Append(note).Append('\n');
				}
				text.Append(ContextText(editLines, e, pairing, true, editLabel));
				text.Append('\n');
				text.Append(ContextText(refLines, r, pairing, false, refLabel));
				text.Append('\n').Append(HintText(hint)).Append('\n');
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText(text.ToString());
				menu.AddChoice(new ConsoleMenuItem("Same line" + Mark(hint, AlignmentHint.Aligned)));
				menu.AddChoice(new ConsoleMenuItem(editLabel + " has this line, " + refLabel + " does not" + Mark(hint, AlignmentHint.MissingEdit)));
				menu.AddChoice(new ConsoleMenuItem(refLabel + " has this line, " + editLabel + " does not" + Mark(hint, AlignmentHint.MissingRef)));
				menu.AddChoice(new ConsoleMenuItem("Out of order: one line's partner is further on..." + MovedMark(hint)));
				menu.AddChoice(new ConsoleMenuItem("Undo the previous decision"));
				menu.AddChoice(new ConsoleMenuItem("Stop here (resume later)"));
				int choice = menu.GetChoice();
				if (choice == 0) {
					pairing.Add(Pair(a.Index, b.Index, "user"));
				}
				if (choice == 1) {
					pairing.Add(Pair(a.Index, -1, PairingEntry.Only));
				}
				if (choice == 2) {
					pairing.Add(Pair(-1, b.Index, PairingEntry.Only));
				}
				if (choice == 3) {
					OutOfOrder(pairing, editLines, e, refLines, r, editLabel, refLabel, hint);
				}
				if (choice == 4) {
					PairingEntry? undone = pairing.UndoLast();
					if (undone == null) {
						Console.WriteLine("Nothing to undo.");
						ConsoleExt.WaitForEnter("continue");
					}
				}
				if (choice == 5 || choice < 0) {
					stop = true;
				}
			}
			return stop;
		}


		/// <summary>
		/// When the model says the windows pair up as far as shown, or diverge only after
		/// some lines, offers those lines as one run. Declining walks them one by one.
		/// </summary>
		/// <returns>True when a run was accepted, so the loop goes round again.</returns>
		private static bool OfferRun(AlignmentPairing pairing, List<AlignmentLine> editLines, int e, List<AlignmentLine> refLines, int r,
			AlignmentHint hint, string editLabel, string refLabel) {
			bool accepted = false;
			int runLength = 0;
			if (hint.Call == AlignmentHint.Aligned) {
				runLength = Math.Min(AlignmentHints.Window(editLines, e, LlmClient.ContextAfter, pairing, true).Count,
					AlignmentHints.Window(refLines, r, LlmClient.ContextAfter, pairing, false).Count);
			}
			if (hint.Call.Length > 0 && hint.Call != AlignmentHint.Aligned && hint.At > 1) {
				runLength = hint.At - 1;
			}
			if (runLength > 0) {
				List<AlignmentLine> windowA = AlignmentHints.Window(editLines, e, runLength, pairing, true);
				List<AlignmentLine> windowB = AlignmentHints.Window(refLines, r, runLength, pairing, false);
				runLength = Math.Min(windowA.Count, windowB.Count);
			}
			if (runLength > 0) {
				List<AlignmentLine> windowA = AlignmentHints.Window(editLines, e, runLength, pairing, true);
				List<AlignmentLine> windowB = AlignmentHints.Window(refLines, r, runLength, pairing, false);
				StringBuilder text = new();
				text.Append("The model says these ").Append(runLength).Append(" line(s) pair up in order:\n");
				for (int at = 0; at < runLength; at++) {
					text.Append("  ").Append(editLabel).Append(' ').Append(windowA[at].Index).Append(": ").Append(AlignmentLines.Preview(windowA[at].Bare, PreviewWidth)).Append('\n');
					text.Append("  ").Append(refLabel).Append(' ').Append(windowB[at].Index).Append(": ").Append(AlignmentLines.Preview(windowB[at].Bare, PreviewWidth)).Append('\n');
				}
				if (hint.Reason.Length > 0) {
					text.Append("Reason: ").Append(hint.Reason).Append('\n');
				}
				bool take = YesNoMenu.Ask("Pair all " + runLength + " as the model recommends? (no walks them one by one)", text.ToString());
				if (take == true) {
					for (int at = 0; at < runLength; at++) {
						pairing.Add(Pair(windowA[at].Index, windowB[at].Index, "model"));
					}
					accepted = true;
				}
				if (take == false) {
					hint.Call = "";
					hint.Problem = "run declined; deciding line by line";
				}
			}
			return accepted;
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
		/// The TGD anchor: both narration, or both named with the same character through
		/// the glossary (one side's Jp name against the other's En name, or aliases).
		/// </summary>
		private static bool SameSpeaker(AlignmentLine a, AlignmentLine b, List<CharacterEntry> characters) {
			bool same = false;
			if (a.HasNametag == false && b.HasNametag == false) {
				same = true;
			}
			if (a.HasNametag == true && b.HasNametag == true) {
				same = string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
				foreach (CharacterEntry entry in characters) {
					if (same == false && Names(entry, a.Name) == true && Names(entry, b.Name) == true) {
						same = true;
					}
				}
			}
			return same;
		}


		private static bool Names(CharacterEntry entry, string name) {
			bool hit = string.Equals(entry.Jp, name, StringComparison.OrdinalIgnoreCase) || string.Equals(entry.En, name, StringComparison.OrdinalIgnoreCase);
			foreach (string alias in entry.Aliases.Split(',')) {
				if (hit == false && alias.Trim().Length > 0 && string.Equals(alias.Trim(), name, StringComparison.OrdinalIgnoreCase)) {
					hit = true;
				}
			}
			return hit;
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
		private static string ContextText(List<AlignmentLine> lines, int at, AlignmentPairing pairing, bool editSide, string label) {
			StringBuilder text = new();
			text.Append("-- ").Append(label).Append(" --\n");
			int before = 0;
			int back = at - 1;
			List<string> above = new();
			while (back >= 0 && before < LlmClient.ContextBefore) {
				above.Insert(0, "      " + lines[back].Index.ToString().PadLeft(5) + "  " + Speaker(lines[back]) + " " + AlignmentLines.Preview(lines[back].Bare, PreviewWidth));
				before++;
				back--;
			}
			foreach (string line in above) {
				text.Append(line).Append('\n');
			}
			text.Append(">>>   ").Append(lines[at].Index.ToString().PadLeft(5)).Append("  ").Append(Speaker(lines[at])).Append(' ').Append(lines[at].Bare).Append('\n');
			List<AlignmentLine> after = AlignmentHints.Window(lines, at + 1, LlmClient.ContextAfter, pairing, editSide);
			foreach (AlignmentLine line in after) {
				text.Append("      ").Append(line.Index.ToString().PadLeft(5)).Append("  ").Append(Speaker(line)).Append(' ').Append(AlignmentLines.Preview(line.Bare, PreviewWidth)).Append('\n');
			}
			return text.ToString();
		}


		private static string HintText(AlignmentHint hint) {
			string text = "Model: no recommendation";
			if (hint.Problem.Length > 0) {
				text += " (" + hint.Problem + ")";
			}
			if (hint.Call.Length > 0) {
				text = "Model: " + hint.Call;
				if (hint.At > 0) {
					text += " at line " + hint.At;
				}
				if (hint.Partner > 0) {
					text += ", partner at line " + hint.Partner;
				}
				if (hint.Reason.Length > 0) {
					text += " - " + hint.Reason;
				}
			}
			return text;
		}


		/// <summary>A mark on the answer the model recommends for the current line.</summary>
		private static string Mark(AlignmentHint hint, string call) {
			string mark = "";
			if (hint.Call == call && hint.At <= 1) {
				mark = "   <- model";
			}
			return mark;
		}


		private static string MovedMark(AlignmentHint hint) {
			string mark = "";
			if ((hint.Call == AlignmentHint.MovedEdit || hint.Call == AlignmentHint.MovedRef) && hint.At <= 1) {
				mark = "   <- model";
			}
			return mark;
		}


		private static string RunNote(int autoRun, int anchorRun) {
			string note = "";
			if (autoRun > 0) {
				note = autoRun + " line(s) matched on text";
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
