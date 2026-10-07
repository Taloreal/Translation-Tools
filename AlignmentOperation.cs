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
			if (problem.Length == 0) {
				key = PickKey(canonical!, other!, out problem);
			}
			if (problem.Length == 0) {
				AlignmentPair? pair = AlignmentPair.Create(canonical!, other!, key, out problem);
				if (pair != null) {
					CheckpointLog.Warning(CheckpointInspector.FolderOf(canonical!.Path), "Align", key + " paired as the reference for \"" + other!.Label + "\" in " + pair.Folder);
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
				AlignmentPair pair = pairs[picked];
				Console.WriteLine("Made:      " + pair.Created);
				Console.WriteLine("File:      " + pair.Key);
				Console.WriteLine("Reference: " + SideLine(pair.CanonicalSerial, pair.CanonicalLabel));
				Console.WriteLine("Editable:  " + SideLine(pair.OtherSerial, pair.OtherLabel));
				Console.WriteLine("Folder:    " + pair.Folder);
				Console.WriteLine("The walk is not built yet; this pair is ready for it.");
				ConsoleExt.WaitForEnter("continue");
			}
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
		private static string PickKey(Checkpoint canonical, Checkpoint other, out string problem) {
			problem = "";
			string key = "";
			List<string> shared = SharedKeys(canonical, other);
			if (shared.Count == 0) {
				problem = "The two splits hold no dialogue file with the same name.";
			}
			if (shared.Count > 0) {
				int picked = PagedPicker.Pick(shared, "Which dialogue file? (" + shared.Count + " in both)");
				if (picked >= 0) {
					key = shared[picked];
				}
				if (picked < 0) {
					problem = "Cancelled. No pair made.";
				}
			}
			return key;
		}


		/// <summary>
		/// The dialogue keys present under split\dialogues\ on both sides, sorted.
		/// </summary>
		private static List<string> SharedKeys(Checkpoint first, Checkpoint second) {
			List<string> firstKeys = DialogueKeys(first);
			List<string> secondKeys = DialogueKeys(second);
			List<string> shared = new();
			foreach (string key in firstKeys) {
				if (secondKeys.Contains(key) == true) {
					shared.Add(key);
				}
			}
			shared.Sort(string.CompareOrdinal);
			return shared;
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
