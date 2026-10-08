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
			menu.AddChoice(new ConsoleMenuItem("Speaker tags: teach a checkpoint from one of its files (optional, sharpens the walk)...").SetActionOnSelect(SpeakerTagsMenu));
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
		/// Optional: teaches a checkpoint how its dialogue files mark the speaker, from one
		/// file the user points at. The rule is learned from repetition, shown, checked once
		/// with the model, and stored in checkpoint.info only on a yes. The walk then knows
		/// that side's speakers. Nothing happens to a checkpoint never taught.
		/// </summary>
		private static void SpeakerTagsMenu() {
			List<Checkpoint> all = CheckpointList.All();
			Checkpoint? checkpoint = PickCheckpoint(all, null, "Teach which checkpoint its speaker tag?");
			if (checkpoint != null) {
				string folder = CheckpointInspector.FolderOf(checkpoint.Path);
				ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
				menu.AddOnDrawMenuAction((shown) => {
					CheckpointInfo info = CheckpointInfo.Load(folder);
					string current = "none; the walk treats every line of this checkpoint as untagged";
					NametagConvention? stored = NametagConvention.FromStored(info.SpeakerTag);
					if (stored != null) {
						current = stored.Opener + "Name" + stored.Closer;
					}
					shown.SetPreChoiceText("-- Speaker tag of " + checkpoint.Label + " --\nNow: " + current + "\n");
				});
				menu.AddChoice(new ConsoleMenuItem("Learn it from one of this checkpoint's dialogue files...").SetActionOnSelect(() => { TeachFromFile(checkpoint, folder); }));
				menu.AddChoice(new ConsoleMenuItem("Copy its speaker tag to another checkpoint of the same game...").SetActionOnSelect(() => { CopyTagTo(checkpoint, folder); }));
				menu.AddChoice(new ConsoleMenuItem("Take the speaker tag from another checkpoint of the same game...").SetActionOnSelect(() => { TakeTagFrom(checkpoint, folder); }));
				menu.AddChoice(new ConsoleMenuItem("Forget the speaker tag: lines count as untagged again").SetActionOnSelect(() => { ForgetTag(folder); }));
				menu.AddChoice(new ConsoleMenuItem("Back"));
				menu.GetChoice();
			}
		}


		/// <summary>
		/// Writes this checkpoint's speaker tag into another checkpoint of the same game.
		/// </summary>
		private static void CopyTagTo(Checkpoint source, string sourceFolder) {
			CheckpointInfo info = CheckpointInfo.Load(sourceFolder);
			if (info.SpeakerTag.Length == 0) {
				Console.WriteLine("\"" + source.Label + "\" has no speaker tag to copy. Learn one first.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (info.SpeakerTag.Length > 0) {
				List<Checkpoint> targets = SameGame(source, info.GameName, false);
				Checkpoint? target = PickCheckpoint(targets, null, "Copy " + info.SpeakerTag[0] + "Name" + info.SpeakerTag[1] + " to which checkpoint of " + info.GameName + "?");
				if (target != null) {
					string targetFolder = CheckpointInspector.FolderOf(target.Path);
					CheckpointInfo targetInfo = CheckpointInfo.Load(targetFolder);
					targetInfo.SpeakerTag = info.SpeakerTag;
					string problem = targetInfo.Save(targetFolder);
					if (problem.Length > 0) {
						Console.WriteLine(problem);
					}
					if (problem.Length == 0) {
						Console.WriteLine("\"" + target.Label + "\" now reads " + info.SpeakerTag[0] + "Name" + info.SpeakerTag[1] + " as its speaker tag.");
					}
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		/// <summary>
		/// Takes the speaker tag of another checkpoint of the same game that has one.
		/// </summary>
		private static void TakeTagFrom(Checkpoint target, string targetFolder) {
			CheckpointInfo info = CheckpointInfo.Load(targetFolder);
			List<Checkpoint> sources = SameGame(target, info.GameName, true);
			if (sources.Count == 0) {
				Console.WriteLine("No other checkpoint of " + info.GameName + " has a speaker tag.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (sources.Count > 0) {
				Checkpoint? source = PickCheckpoint(sources, null, "Take the speaker tag from which checkpoint of " + info.GameName + "?");
				if (source != null) {
					CheckpointInfo sourceInfo = CheckpointInfo.Load(CheckpointInspector.FolderOf(source.Path));
					info.SpeakerTag = sourceInfo.SpeakerTag;
					string problem = info.Save(targetFolder);
					if (problem.Length > 0) {
						Console.WriteLine(problem);
					}
					if (problem.Length == 0) {
						Console.WriteLine("\"" + target.Label + "\" now reads " + info.SpeakerTag[0] + "Name" + info.SpeakerTag[1] + " as its speaker tag.");
					}
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		/// <summary>
		/// The other checkpoints whose info names the same game, optionally only those that
		/// have a speaker tag. A checkpoint with no game name matches nothing.
		/// </summary>
		private static List<Checkpoint> SameGame(Checkpoint except, string gameName, bool withTagOnly) {
			List<Checkpoint> same = new();
			if (gameName.Length > 0) {
				foreach (Checkpoint checkpoint in CheckpointList.All()) {
					if (checkpoint.Serial != except.Serial) {
						CheckpointInfo info = CheckpointInfo.Load(CheckpointInspector.FolderOf(checkpoint.Path));
						bool sameGame = string.Equals(info.GameName, gameName, StringComparison.OrdinalIgnoreCase);
						bool hasTag = info.SpeakerTag.Length == 2;
						if (sameGame == true && (withTagOnly == false || hasTag == true)) {
							same.Add(checkpoint);
						}
					}
				}
			}
			return same;
		}


		/// <summary>
		/// Picks a dialogue file, learns the tag from it, asks the model once, and stores
		/// the rule after the user's yes.
		/// </summary>
		private static void TeachFromFile(Checkpoint checkpoint, string folder) {
			List<string> keys = DialogueKeys(checkpoint);
			keys.Sort(string.CompareOrdinal);
			int picked = PagedPicker.Pick(keys, "Learn the speaker tag from which file of " + checkpoint.Label + "? (" + keys.Count + ")");
			if (picked >= 0) {
				string path = AlignmentLines.DialoguePath(checkpoint, keys[picked]);
				NametagConvention? learned = NametagConvention.Learn(AlignmentLines.ReadTexts(path), Glossary.Characters(folder));
				if (learned == null) {
					Console.WriteLine("No speaker tag could be learned from " + keys[picked] + ". Without a glossary that needs ten tagged lines with four names each appearing twice; with one, five lines naming two known characters. Try a longer file, or fill the glossary first.");
					ConsoleExt.WaitForEnter("continue");
				}
				if (learned != null) {
					if (AlignmentHints.GaveUp == false) {
						learned.ConfirmWithModel(checkpoint.Label);
					}
					string names = "";
					int shown = 0;
					foreach (string name in learned.Names) {
						if (shown < 10) {
							if (names.Length > 0) {
								names += ", ";
							}
							names += name;
							shown++;
						}
					}
					bool adopt = YesNoMenu.Ask("Use " + learned.Opener + "Name" + learned.Closer + " as " + checkpoint.Label + "'s speaker tag?",
						"Learned from " + keys[picked] + ": " + learned.Describe() + "\nNames seen: " + names, true);
					if (adopt == true) {
						CheckpointInfo info = CheckpointInfo.Load(folder);
						info.SpeakerTag = learned.Stored;
						string problem = info.Save(folder);
						if (problem.Length > 0) {
							Console.WriteLine(problem);
						}
						if (problem.Length == 0) {
							Console.WriteLine("Stored. The walk now reads " + learned.Opener + "Name" + learned.Closer + " as the speaker on " + checkpoint.Label + ".");
						}
						ConsoleExt.WaitForEnter("continue");
					}
				}
			}
		}


		private static void ForgetTag(string folder) {
			CheckpointInfo info = CheckpointInfo.Load(folder);
			info.SpeakerTag = "";
			string problem = info.Save(folder);
			if (problem.Length > 0) {
				Console.WriteLine(problem);
			}
			if (problem.Length == 0) {
				Console.WriteLine("Forgotten. Every line of this checkpoint counts as untagged in the walk.");
			}
			ConsoleExt.WaitForEnter("continue");
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
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				ConsoleExt.WaitForEnter("continue");
			}
			if (problem.Length == 0) {
				AlignmentWalk.Run(pair, edit!, reference!);
			}
		}


		/// <summary>
		/// Forgets the pairing file and the status. Choosing the row is the decision; the row
		/// says what it forgets, so nothing is asked again.
		/// </summary>
		private static void ResetPair(AlignmentPair pair) {
			AlignmentPairing pairing = AlignmentPairing.Load(pair.Folder);
			int forgotten = pairing.Entries.Count;
			pairing.Reset();
			Console.WriteLine("Progress reset: " + forgotten + " decision(s) forgotten. The next walk starts from the first line.");
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// A paged pick over the checkpoints, leaving one out.
		/// </summary>
		/// <param name="all">Every checkpoint.</param>
		/// <param name="except">One not to offer, or null.</param>
		/// <param name="title">Printed above the list.</param>
		/// <returns>The pick, or null for Back.</returns>
		private static Checkpoint? PickCheckpoint(List<Checkpoint> all, Checkpoint? except, string title) {
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
			List<string> editKeys = DialogueKeys(other);
			List<string> referenceKeys = DialogueKeys(canonical);
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


		private static List<string> DialogueKeys(Checkpoint checkpoint) {
			List<string> keys = new();
			string dialogues = Path.Combine(CheckpointInspector.FolderOf(checkpoint.Path), CheckpointInspector.SplitFolder, NScripterSplit.DialoguesFolder);
			if (Directory.Exists(dialogues) == true) {
				foreach (string file in Directory.GetFiles(dialogues, "*.txt")) {
					keys.Add(Path.GetFileNameWithoutExtension(file));
				}
			}
			return keys;
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
