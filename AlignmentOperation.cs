// File: AlignmentOperation.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Alignment, a top-level menu beside Operations: pair ONE dialogue file between two checkpoints of the same game so
	/// its lines can be walked side by side. A pair is two checkpoints plus a key. Alignment never reads the selector and never asks which engine
	/// or language either side is: any two split checkpoints may be paired. The locks decide
	/// which side is the reference (canonical): the locked one. When neither is locked the
	/// user names the reference and it is locked; when both are, there is nothing to edit.
	/// </summary>
	public static class AlignmentOperation {

		/// <summary>
		/// Shows the Align menu until the user chooses Back.
		/// </summary>
		public static void Run() {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction((shown) => {
				shown.SetPreChoiceText("-- Alignment --\nPairs: " + AlignmentPair.All().Count + "   under " + AlignmentRoot.Folder + "\n");
			});
			menu.AddChoice(new ConsoleMenuItem("Start a new pair...").SetActionOnSelect(StartPair));
			menu.AddChoice(new ConsoleMenuItem("Open a pair...").SetActionOnSelect(OpenPair));
			menu.AddChoice(new ConsoleMenuItem("Delete a pair: its folder, pairings and backups go...").SetActionOnSelect(DeletePair));
			menu.AddChoice(new ConsoleMenuItem("Settings: speaker-order window, pairing without asking...").SetActionOnSelect(SettingsMenu));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Picks two split checkpoints, settles which is the reference, and makes the pair folder.
		/// </summary>
		private static void StartPair() {
			List<Checkpoint> all = CheckpointList.All();
			string problem = "";
			if (all.Count < 2) {
				problem = "Two checkpoints are needed; there are " + all.Count + ".";
			}
			Checkpoint? first = null;
			Checkpoint? second = null;
			if (problem.Length == 0) {
				first = PickCheckpoint(all, null, "First checkpoint of the pair");
				if (first == null) {
					problem = "Cancelled. No pair made.";
				}
			}
			if (problem.Length == 0) {
				second = PickCheckpoint(all, first, "Second checkpoint, to pair with " + first!.Label);
				if (second == null) {
					problem = "Cancelled. No pair made.";
				}
			}
			if (problem.Length == 0) {
				problem = MustBeSplit(first!);
			}
			if (problem.Length == 0) {
				problem = MustBeSplit(second!);
			}
			if (problem.Length == 0 && SyncGlossaries(first!, second!) == false) {
				problem = "Cancelled: the two glossaries differ and neither was copied. No pair made.";
			}
			Checkpoint? canonical = null;
			Checkpoint? other = null;
			if (problem.Length == 0) {
				problem = SettleReference(first!, second!, out canonical, out other);
			}
			string key = "";
			string refKey = "";
			if (problem.Length == 0) {
				key = PickKey(canonical!, other!, out refKey, out problem);
			}
			if (problem.Length == 0) {
				AlignmentPair? pair = AlignmentPair.Create(canonical!, other!, key, refKey, out problem);
				if (pair != null) {
					CheckpointLog.Warning(CheckpointInspector.FolderOf(canonical!.Path), "Align", refKey + " paired as the reference for \"" + other!.Label + "\" " + key + " in " + pair.Folder);
					CheckpointLog.Warning(CheckpointInspector.FolderOf(other.Path), "Align", key + " paired against the reference \"" + canonical.Label + "\" in " + pair.Folder);
					Console.WriteLine("Pair made: " + pair.Describe());
					Console.WriteLine("Folder: " + pair.Folder);
				}
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
			}
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// Lists the pairs and shows the one picked. The walk itself comes later; for now a
		/// pair opens to its facts.
		/// </summary>
		private static void OpenPair() {
			List<AlignmentPair> pairs = AlignmentPair.All();
			List<string> rows = new();
			foreach (AlignmentPair pair in pairs) {
				rows.Add(pair.Describe());
			}
			int picked = PagedPicker.Pick(rows, "Pairs, newest first");
			if (picked >= 0) {
				PairMenu(pairs[picked]);
			}
		}


		/// <summary>
		/// The walk's own settings, each row showing its value.
		/// </summary>
		private static void SettingsMenu() {
			ConsoleMenuItem windowItem = new("");
			ConsoleMenuItem runItem = new("");
			ConsoleMenuItem clipsItem = new("");
			ConsoleMenuItem shortItem = new("");
			ConsoleMenuItem learnItem = new("");
			ConsoleMenuItem modelRunItem = new("");
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction((shown) => {
				windowItem.SetText("Speaker-order window each side (0 = off): " + AlignmentSettings.SpeakerWindow);
				runItem.SetText("Long stretches: speaker order + audio matching over N lines pair with no one asked (0 = off): " + AlignmentSettings.AutoPairRun);
				modelRunItem.SetText("Short stretches: the same over N lines with the model agreeing, N below the long one (0 = off): " + AlignmentSettings.ModelPairRun);
				clipsItem.SetText("Fewest shared voice clips in such a stretch: " + AlignmentSettings.AutoPairClips);
				shortItem.SetText("Short identical lines pair on their own when the speaker order matches for N more lines: " + AlignmentSettings.ShortLineNeighbours);
				learnItem.SetText("Offer to learn a speaker when the words match but the tags differ for N lines: " + AlignmentSettings.LearnSpeakerLines);
				shown.SetPreChoiceText("-- Alignment settings --\nThese need both sides taught their speaker tags; the audio ones need at least one SiglusEngine side.\n");
			});
			menu.AddChoice(windowItem.SetActionOnSelect(() => { LlmMenu.SetWholeNumber("Speaker-order window each side, 0 for off", AlignmentSettings.SpeakerWindow, 0, AlignmentSettings.MostLines, (value) => { AlignmentSettings.SpeakerWindow = value; }); }));
			menu.AddChoice(runItem.SetActionOnSelect(SetLongStretch));
			menu.AddChoice(modelRunItem.SetActionOnSelect(SetShortStretch));
			menu.AddChoice(clipsItem.SetActionOnSelect(() => { LlmMenu.SetWholeNumber("Fewest shared voice clips in the matched stretch", AlignmentSettings.AutoPairClips, 1, AlignmentSettings.MostLines, (value) => { AlignmentSettings.AutoPairClips = value; }); }));
			menu.AddChoice(shortItem.SetActionOnSelect(() => { LlmMenu.SetWholeNumber("Matching speaker-order lines around a short identical line", AlignmentSettings.ShortLineNeighbours, 1, AlignmentSettings.MostLines, (value) => { AlignmentSettings.ShortLineNeighbours = value; }); }));
			menu.AddChoice(learnItem.SetActionOnSelect(() => { LlmMenu.SetWholeNumber("Matching lines with differing tags before the tool offers to learn the speaker", AlignmentSettings.LearnSpeakerLines, 1, AlignmentSettings.MostLines, (value) => { AlignmentSettings.LearnSpeakerLines = value; }); }));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// The long-stretch threshold: must stay above the short-stretch one, since the short
		/// one is the same rule with the model added. 0 turns the long path off.
		/// </summary>
		private static void SetLongStretch() {
			int least = AlignmentSettings.ModelPairRun + 1;
			if (AlignmentSettings.ModelPairRun == 0) {
				least = 1;
			}
			Console.WriteLine("The short-stretch setting is " + AlignmentSettings.ModelPairRun + "; the long one must be above it, or 0 for off.");
			LlmMenu.SetWholeNumber("Long stretch, lines of matching speaker order with agreeing audio that pair with no one asked (0 for off, else at least " + least + ")",
				AlignmentSettings.AutoPairRun, 0, AlignmentSettings.MostLines, (value) => { StoreLongStretch(value, least); });
		}


		private static void StoreLongStretch(int value, int least) {
			if (value == 0 || value >= least) {
				AlignmentSettings.AutoPairRun = value;
			}
			if (value != 0 && value < least) {
				Console.WriteLine("Not stored: the long stretch must be above the short one (" + AlignmentSettings.ModelPairRun + ").");
			}
		}


		/// <summary>
		/// The short-stretch threshold: must stay below the long-stretch one. 0 turns the
		/// model-agreeing path off.
		/// </summary>
		private static void SetShortStretch() {
			int most = AlignmentSettings.AutoPairRun - 1;
			if (AlignmentSettings.AutoPairRun == 0) {
				most = AlignmentSettings.MostLines;
			}
			Console.WriteLine("The long-stretch setting is " + AlignmentSettings.AutoPairRun + "; the short one must be below it, or 0 for off.");
			LlmMenu.SetWholeNumber("Short stretch, lines of matching speaker order with agreeing audio and the model agreeing (0 for off, else at most " + most + ")",
				AlignmentSettings.ModelPairRun, 0, AlignmentSettings.MostLines, (value) => { StoreShortStretch(value, most); });
		}


		private static void StoreShortStretch(int value, int most) {
			if (value <= most) {
				AlignmentSettings.ModelPairRun = value;
			}
			if (value > most) {
				Console.WriteLine("Not stored: the short stretch must be below the long one (" + AlignmentSettings.AutoPairRun + ").");
			}
		}


		/// <summary>
		/// Removes a pair's folder with everything in it. The picker row names what goes, so
		/// picking it is the decision; the pair's checkpoints are untouched.
		/// </summary>
		private static void DeletePair() {
			List<AlignmentPair> pairs = AlignmentPair.All();
			List<string> rows = new();
			foreach (AlignmentPair pair in pairs) {
				rows.Add(pair.Describe());
			}
			int picked = PagedPicker.Pick(rows, "Delete which pair? Its folder, pairings and backups go; the checkpoints stay.");
			if (picked >= 0) {
				AlignmentPair pair = pairs[picked];
				try {
					Directory.Delete(pair.Folder, true);
					Console.WriteLine("Deleted " + pair.Describe());
				}
				catch (Exception exception) {
					Console.WriteLine("Could not delete " + pair.Folder + ": " + exception.Message);
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}


		/// <summary>
		/// One pair's menu: its facts and progress above, then walk (or resume), reset, back.
		/// </summary>
		private static void PairMenu(AlignmentPair pair) {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction((shown) => {
				AlignmentPairing pairing = AlignmentPairing.Load(pair.Folder);
				shown.SetPreChoiceText("-- Pair made " + pair.Created + " --\n"
					+ "File:      " + pair.Key + "\n"
					+ "Reference: " + SideLine(pair.CanonicalSerial, pair.CanonicalLabel) + "\n"
					+ "Editable:  " + SideLine(pair.OtherSerial, pair.OtherLabel) + "\n"
					+ "Folder:    " + pair.Folder + "\n"
					+ "Progress:  " + pairing.Status + ", " + pairing.PairedCount + " pairs, " + pairing.EditOnlyCount + " only on the editable side, "
					+ pairing.RefOnlyCount + " only on the reference\n"
					+ "Left:      " + RemainingText(pair, pairing) + "\n");
			});
			menu.AddChoice(new ConsoleMenuItem("Walk the lines (resumes where it stopped)").SetActionOnSelect(() => { WalkPair(pair); }));
			menu.AddChoice(new ConsoleMenuItem("Apply the alignment (once it is complete): renumber both files so index n is the same line on both sides...").SetActionOnSelect(() => { ApplyPair(pair); }));
			menu.AddChoice(new ConsoleMenuItem("Repair speaker tags from the alignment (once it is complete): pick whose tags are the standard...").SetActionOnSelect(() => { RepairPair(pair); }));
			menu.AddChoice(new ConsoleMenuItem("Reset progress: forget every pairing of this file").SetActionOnSelect(() => { ResetPair(pair); }));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// How many lines each side still has undecided, out of its total. A side whose
		/// checkpoint or file is gone says so instead of a count.
		/// </summary>
		private static string RemainingText(AlignmentPair pair, AlignmentPairing pairing) {
			return SideRemaining(pair.OtherSerial, pair.Key, pairing, true) + " on the editable side, "
				+ SideRemaining(pair.CanonicalSerial, pair.RefKey, pairing, false) + " on the reference";
		}


		private static string SideRemaining(string serial, string key, AlignmentPairing pairing, bool editSide) {
			string text = "(checkpoint missing)";
			Checkpoint? checkpoint = CheckpointList.FindBySerial(serial);
			if (checkpoint != null) {
				string path = AlignmentLines.DialoguePath(checkpoint, key);
				text = "(file missing)";
				if (File.Exists(path) == true) {
					List<AlignmentLine> lines = AlignmentLines.Read(path);
					int left = 0;
					foreach (AlignmentLine line in lines) {
						bool decided = pairing.EditDecided(line.Index);
						if (editSide == false) {
							decided = pairing.RefDecided(line.Index);
						}
						if (decided == false) {
							left++;
						}
					}
					text = left + " of " + lines.Count + " undecided";
				}
			}
			return text;
		}


		/// <summary>
		/// Finds both checkpoints by serial and starts or resumes the walk.
		/// </summary>
		private static void WalkPair(AlignmentPair pair) {
			Checkpoint? reference = CheckpointList.FindBySerial(pair.CanonicalSerial);
			Checkpoint? edit = CheckpointList.FindBySerial(pair.OtherSerial);
			string problem = "";
			if (reference == null) {
				problem = "No checkpoint has the reference serial " + pair.CanonicalSerial + " any more.";
			}
			if (problem.Length == 0 && edit == null) {
				problem = "No checkpoint has the editable serial " + pair.OtherSerial + " any more.";
			}
			if (problem.Length == 0 && reference!.Writable == true) {
				problem = "The reference \"" + reference.Label + "\" is not locked. Lock it, or start a new pair.";
			}
			if (problem.Length == 0 && AlignmentPairing.Load(pair.Folder).Status == AlignmentPairing.Applied) {
				problem = "This alignment was applied: the files are renumbered and the walk's numbers no longer exist. Delete the pair and start a new one to realign.";
			}
			if (problem.Length == 0 && SyncGlossaries(edit!, reference!) == false) {
				problem = "The two glossaries differ and neither was copied. The walk needs one glossary on both sides.";
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				ConsoleExt.WaitForEnter("continue");
			}
			if (problem.Length == 0) {
				AlignmentWalk.Run(pair, edit!, reference!);
			}
		}


		/// <summary>
		/// Resolves the pair's two checkpoints and hands them to Apply, which refuses on its
		/// own until the alignment is complete, and once it has been applied.
		/// </summary>
		private static void ApplyPair(AlignmentPair pair) {
			Checkpoint? reference = CheckpointList.FindBySerial(pair.CanonicalSerial);
			Checkpoint? edit = CheckpointList.FindBySerial(pair.OtherSerial);
			string problem = "";
			if (reference == null) {
				problem = "No checkpoint has the reference serial " + pair.CanonicalSerial + " any more.";
			}
			if (problem.Length == 0 && edit == null) {
				problem = "No checkpoint has the editable serial " + pair.OtherSerial + " any more.";
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				ConsoleExt.WaitForEnter("continue");
			}
			if (problem.Length == 0) {
				AlignmentApply.Run(pair, edit!, reference!);
			}
		}


		/// <summary>
		/// Resolves the pair's two checkpoints and hands them to the speaker repair, which
		/// refuses on its own until the alignment is complete.
		/// </summary>
		private static void RepairPair(AlignmentPair pair) {
			Checkpoint? reference = CheckpointList.FindBySerial(pair.CanonicalSerial);
			Checkpoint? edit = CheckpointList.FindBySerial(pair.OtherSerial);
			string problem = "";
			if (reference == null) {
				problem = "No checkpoint has the reference serial " + pair.CanonicalSerial + " any more.";
			}
			if (problem.Length == 0 && edit == null) {
				problem = "No checkpoint has the editable serial " + pair.OtherSerial + " any more.";
			}
			if (problem.Length == 0 && SyncGlossaries(edit!, reference!) == false) {
				problem = "The two glossaries differ and neither was copied. The repair reads speakers through one glossary.";
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				ConsoleExt.WaitForEnter("continue");
			}
			if (problem.Length == 0) {
				SpeakerRepair.Run(pair, edit!, reference!);
			}
		}


		/// <summary>
		/// Forgets the pairing file and the status. Choosing the row is the decision; the row
		/// says what it forgets, so nothing is asked again.
		/// </summary>
		private static void ResetPair(AlignmentPair pair) {
			AlignmentPairing pairing = AlignmentPairing.Load(pair.Folder);
			if (pairing.Status == AlignmentPairing.Applied) {
				Console.WriteLine("This alignment was applied; its record stays as the history of what was renumbered. Delete the pair and start a new one to realign.");
			}
			if (pairing.Status != AlignmentPairing.Applied) {
				int forgotten = pairing.Entries.Count;
				pairing.Reset();
				Console.WriteLine("Progress reset: " + forgotten + " decision(s) forgotten. The next walk starts from the first line.");
			}
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// A paged pick over the checkpoints, leaving one out.
		/// </summary>
		/// <param name="all">Every checkpoint.</param>
		/// <param name="except">One not to offer, or null.</param>
		/// <param name="title">Printed above the list.</param>
		/// <returns>The pick, or null for Back.</returns>
		public static Checkpoint? PickCheckpoint(List<Checkpoint> all, Checkpoint? except, string title) {
			List<Checkpoint> offered = new();
			List<string> rows = new();
			foreach (Checkpoint checkpoint in all) {
				bool skip = except != null && checkpoint.Serial == except.Serial;
				if (skip == false) {
					offered.Add(checkpoint);
					CheckpointState state = CheckpointInspector.Inspect(checkpoint.Path);
					rows.Add(checkpoint.Label.PadRight(20) + " #" + checkpoint.Serial + "  " + state.Describe().PadRight(26) + "  " + checkpoint.StateWord);
				}
			}
			Checkpoint? picked = null;
			int index = PagedPicker.Pick(rows, title);
			if (index >= 0) {
				picked = offered[index];
			}
			return picked;
		}


		/// <summary>
		/// A pair walks on one glossary: both checkpoints must hold the same one, byte for
		/// byte, so a tag resolves the same way on either side and a correction writes the
		/// shared written name. When they differ the user picks whose glossary is copied over
		/// the other's, the rows naming what is replaced, or cancels.
		/// </summary>
		/// <returns>True when the glossaries are the same afterwards.</returns>
		public static bool SyncGlossaries(Checkpoint first, Checkpoint second) {
			string firstFolder = CheckpointInspector.FolderOf(first.Path);
			string secondFolder = CheckpointInspector.FolderOf(second.Path);
			bool same = Glossary.SameContent(firstFolder, secondFolder);
			if (same == false) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText("The two glossaries differ, and a pair walks on one glossary.\n"
					+ "\"" + first.Label + "\": " + Glossary.Characters(firstFolder).Count + " characters, " + Glossary.Rules(firstFolder).Count + " rules\n"
					+ "\"" + second.Label + "\": " + Glossary.Characters(secondFolder).Count + " characters, " + Glossary.Rules(secondFolder).Count + " rules\n");
				menu.AddChoice(new ConsoleMenuItem("Copy \"" + first.Label + "\"'s glossary over \"" + second.Label + "\"'s (" + second.Label + "'s is replaced)"));
				menu.AddChoice(new ConsoleMenuItem("Copy \"" + second.Label + "\"'s glossary over \"" + first.Label + "\"'s (" + first.Label + "'s is replaced)"));
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				int choice = menu.GetChoice();
				string problem = "";
				if (choice == 0) {
					problem = Glossary.CopyOver(firstFolder, secondFolder);
					if (problem.Length == 0) {
						CheckpointLog.Warning(secondFolder, "Glossary", "glossary replaced by a copy of \"" + first.Label + "\"'s, for alignment");
					}
				}
				if (choice == 1) {
					problem = Glossary.CopyOver(secondFolder, firstFolder);
					if (problem.Length == 0) {
						CheckpointLog.Warning(firstFolder, "Glossary", "glossary replaced by a copy of \"" + second.Label + "\"'s, for alignment");
					}
				}
				if (problem.Length > 0) {
					Console.WriteLine(problem);
					ConsoleExt.WaitForEnter("continue");
				}
				same = (choice == 0 || choice == 1) && problem.Length == 0;
			}
			return same;
		}


		/// <summary>
		/// A pair needs dialogue files on both sides, so both must be split. A sound, unsplit,
		/// writable checkpoint is offered a split on the spot; declining cancels the pair.
		/// Anything else that is not split is refused with its state.
		/// </summary>
		/// <returns>Empty when split; otherwise why not.</returns>
		private static string MustBeSplit(Checkpoint checkpoint) {
			string problem = "";
			CheckpointState state = CheckpointInspector.Inspect(checkpoint.Path);
			bool canSplitNow = state.Form == CheckpointForm.Unsplit && state.NeedsRecovery == false && checkpoint.Writable == true;
			if (state.Form != CheckpointForm.Split && canSplitNow == false) {
				problem = "\"" + checkpoint.Label + "\" is " + state.Describe() + " (" + checkpoint.StateWord + "); both sides must be split.";
			}
			if (state.Form != CheckpointForm.Split && canSplitNow == true) {
				bool splitNow = YesNoMenu.Ask("Split it now?", "\"" + checkpoint.Label + "\" is not split yet, and a pair needs its dialogue files.");
				if (splitNow == false) {
					problem = "Cancelled. No pair made.";
				}
				if (splitNow == true) {
					problem = SplitOperation.Split(checkpoint, false, Console.WriteLine);
					if (problem.Length == 0) {
						Console.WriteLine("Split \"" + checkpoint.Label + "\".");
						if (state.Engine == CheckpointEngine.Siglus) {
							SplitOperation.AskWrapColumn(checkpoint);
						}
					}
					CheckpointWatch.MarkStale();
				}
			}
			return problem;
		}


		/// <summary>
		/// The locks decide the reference. One locked: that one. Neither: the user names it
		/// and it is locked here. Both: refused.
		/// </summary>
		/// <returns>Empty when settled; otherwise why not.</returns>
		private static string SettleReference(Checkpoint first, Checkpoint second, out Checkpoint? canonical, out Checkpoint? other) {
			string problem = "";
			canonical = null;
			other = null;
			if (first.Writable == false && second.Writable == false) {
				problem = "Both are locked; nothing could be edited. Unlock the one you mean to work on.";
			}
			if (problem.Length == 0 && first.Writable == false) {
				canonical = first;
				other = second;
			}
			if (problem.Length == 0 && canonical == null && second.Writable == false) {
				canonical = second;
				other = first;
			}
			if (problem.Length == 0 && canonical == null) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText("Neither is locked. The reference is read, never edited, and is LOCKED now;\nthe other side is the one you will edit.\nWhich is the reference?");
				menu.AddChoice(new ConsoleMenuItem(first.Label + " #" + first.Serial));
				menu.AddChoice(new ConsoleMenuItem(second.Label + " #" + second.Serial));
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				int choice = menu.GetChoice();
				if (choice == 0) {
					canonical = first;
					other = second;
				}
				if (choice == 1) {
					canonical = second;
					other = first;
				}
				if (canonical == null) {
					problem = "Cancelled. No pair made.";
				}
				if (canonical != null) {
					Checkpoint locked = canonical;
					locked.Writable = false;
					CheckpointList.Update(canonical.Label, locked);
					Console.WriteLine("Locked \"" + canonical.Label + "\" as the reference.");
				}
			}
			return problem;
		}


		/// <summary>
		/// The dialogue file to align: a paged pick over the keys both splits hold.
		/// </summary>
		/// <param name="problem">Why not, when no key could be picked; empty otherwise.</param>
		/// <returns>The key, or empty when problem says why.</returns>
		private static string PickKey(Checkpoint canonical, Checkpoint other, out string refKey, out string problem) {
			problem = "";
			string key = "";
			refKey = "";
			List<string> editKeys = AlignmentLines.DialogueKeys(other);
			List<string> referenceKeys = AlignmentLines.DialogueKeys(canonical);
			editKeys.Sort(string.CompareOrdinal);
			referenceKeys.Sort(string.CompareOrdinal);
			if (editKeys.Count == 0 || referenceKeys.Count == 0) {
				problem = "One of the splits holds no dialogue files.";
			}
			if (problem.Length == 0) {
				int picked = PagedPicker.Pick(editKeys, "Which dialogue file of " + other.Label + "? (" + editKeys.Count + ")");
				if (picked >= 0) {
					key = editKeys[picked];
				}
				if (picked < 0) {
					problem = "Cancelled. No pair made.";
				}
			}
			if (problem.Length == 0) {
				// The reference usually holds a file of the same name, but not always the same
				// scene: a version may keep an empty file under the old name and put the scene
				// under another. So the same name is offered, never assumed, and a no - or no
				// such file - opens the reference's own catalogue.
				bool useSameName = false;
				if (referenceKeys.Contains(key) == true) {
					useSameName = YesNoMenu.Ask("Use " + canonical.Label + "'s " + key + " as the reference file for " + other.Label + "'s " + key + "?",
						"The reference holds a file of the same name. Say no if the scene lives under another name there.");
				}
				if (useSameName == true) {
					refKey = key;
				}
				if (useSameName == false) {
					int picked = PagedPicker.Pick(referenceKeys, "Which file of " + canonical.Label + " is " + other.Label + "'s " + key + "? (" + referenceKeys.Count + ")");
					if (picked >= 0) {
						refKey = referenceKeys[picked];
					}
					if (picked < 0) {
						problem = "Cancelled. No pair made.";
					}
				}
			}
			return key;
		}


		private static string SideLine(string serial, string labelThen) {
			string text = labelThen + " #" + serial + " (no checkpoint has this serial any more)";
			Checkpoint? now = CheckpointList.FindBySerial(serial);
			if (now != null) {
				text = now.Label + " #" + serial + "  " + now.StateWord + "  " + now.Path;
			}
			return text;
		}
	}
}
