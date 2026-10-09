// File: SplitOperation.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Split: extract\ -> split\. Runs on an unsplit checkpoint. A split one keeps its split,
	/// since splitting again would orphan the edits in it, unless start over is on, which
	/// offers to back the split up or discard it first. NScripter goes through
	/// NScripterSplit, Siglus through SiglusSplit; a Siglus split ends by asking for the
	/// checkpoint's governing wrap width.
	/// </summary>
	public static class SplitOperation {

		/// <summary>
		/// Runs Split on the selected checkpoint, printing as it goes, and pauses at the end.
		/// </summary>
		/// <param name="startOver">True to discard an existing split (after offering a backup) instead of refusing.</param>
		public static void Run(bool startOver) {
			Checkpoint? checkpoint = CheckpointList.Selected();
			string problem = "No checkpoint is selected.";
			string folder = "";
			if (checkpoint != null) {
				folder = CheckpointInspector.FolderOf(checkpoint.Path);
				problem = Split(checkpoint, startOver, Console.WriteLine);
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				if (problem.StartsWith("Cancelled") == false) {
					CheckpointLog.Error(folder, "Split", problem);
				}
			}
			if (problem.Length == 0) {
				Console.WriteLine("Split into " + Path.Combine(folder, CheckpointInspector.SplitFolder));
				if (CheckpointInspector.Inspect(checkpoint!.Path).Engine == CheckpointEngine.Siglus) {
					AskWrapColumn(checkpoint);
				}
			}
			CheckpointWatch.MarkStale();
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// After a Siglus split: asks for the checkpoint's governing wrap column, the display
		/// width Join wraps prose at in any scene that carries no "// wrap" control code.
		/// Blank keeps what is recorded; 0 clears it.
		/// </summary>
		public static void AskWrapColumn(Checkpoint checkpoint) {
			AskWrapColumn(checkpoint, false);
		}


		/// <summary>
		/// The wrap-width question. A master like one the width was chosen for before -
		/// eighteen or more of its twenty slice hashes shared - gets that width without a
		/// question when the checkpoint has none yet and the caller allows it. An answer is
		/// remembered against the master's hash set for the next checkpoint of this game.
		/// </summary>
		/// <param name="checkpoint">The checkpoint; its master's hash set keys the memory.</param>
		/// <param name="alwaysAsk">True to ask even when a remembered width applies (the menu); false after a split.</param>
		public static void AskWrapColumn(Checkpoint checkpoint, bool alwaysAsk) {
			string[] hashSet = MasterHashSet(checkpoint);
			int rememberedColumn = Checkpoint.NoWrapColumn;
			bool remembered = false;
			if (hashSet.Length > 0) {
				remembered = WrapWidthMemory.Find(hashSet, out rememberedColumn);
			}
			bool applied = false;
			if (remembered == true && alwaysAsk == false && checkpoint.WrapColumn == Checkpoint.NoWrapColumn) {
				checkpoint.WrapColumn = rememberedColumn;
				CheckpointList.Update(checkpoint.Label, checkpoint);
				Console.WriteLine("Governing wrap width: " + WidthWord(rememberedColumn) + ", the choice made for an archive like this one before. Change it under the Checkpoints menu.");
				applied = true;
			}
			if (applied == false) {
				string current = WidthWord(checkpoint.WrapColumn);
				Console.WriteLine("Join can wrap English prose to the game's text window. A scene can say its own width with a \"// wrap 60\" comment;");
				Console.WriteLine("this governing width covers scenes that do not. Current: " + current + ".");
				string typed = ConsoleExt.ReadLine("Governing wrap width in characters (blank to keep, 0 for none): ", -1, false).Trim();
				if (typed.Length > 0) {
					bool parsed = int.TryParse(typed, out int column);
					if (parsed == false || column < 0) {
						Console.WriteLine("Not a width. Kept " + current + ".");
					}
					if (parsed == true && column >= 0) {
						checkpoint.WrapColumn = column;
						CheckpointList.Update(checkpoint.Label, checkpoint);
						if (hashSet.Length > 0) {
							WrapWidthMemory.Record(hashSet, column);
						}
						Console.WriteLine("Governing wrap width: " + WidthWord(column) + ".");
					}
				}
			}
		}


		private static string WidthWord(int column) {
			string word = "none";
			if (column > Checkpoint.NoWrapColumn) {
				word = column.ToString();
			}
			return word;
		}


		/// <summary>
		/// The hash set of the checkpoint's Siglus master, or empty when it cannot be read.
		/// </summary>
		private static string[] MasterHashSet(Checkpoint checkpoint) {
			string[] hashSet = new string[0];
			string master = Path.Combine(CheckpointInspector.FolderOf(checkpoint.Path), "Scene.pck");
			if (File.Exists(master) == true) {
				try {
					hashSet = BuildModeService.SegmentHashes(master);
				}
				catch (Exception) {
					// Unreadable master: the question is asked and nothing is remembered.
				}
			}
			return hashSet;
		}


		/// <summary>
		/// Does the split. Printing goes through onLine, so this can be exercised without a
		/// console.
		/// </summary>
		/// <param name="checkpoint">The checkpoint to split.</param>
		/// <param name="startOver">True to discard an existing split (after offering a backup) instead of refusing.</param>
		/// <param name="onLine">Receives progress.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Split(Checkpoint checkpoint, bool startOver, Action<string> onLine) {
			string problem = "";
			string folder = CheckpointInspector.FolderOf(checkpoint.Path);
			CheckpointState state = CheckpointInspector.Inspect(checkpoint.Path);

			if (checkpoint.Writable == false) {
				problem = "\"" + checkpoint.Label + "\" is locked.";
			}
			if (problem.Length == 0 && state.Form == CheckpointForm.Invalid) {
				problem = "\"" + checkpoint.Label + "\" is invalid: " + state.Reason;
			}
			if (problem.Length == 0 && state.NeedsRecovery == true) {
				problem = "\"" + checkpoint.Label + "\" needs recovery first.";
			}
			if (problem.Length == 0 && state.Form == CheckpointForm.Split && startOver == false) {
				problem = "\"" + checkpoint.Label + "\" is already split. Splitting again would orphan the split. Turn on start over (Left or Right on Split) to discard it.";
			}
			if (problem.Length == 0 && state.Form == CheckpointForm.Split && startOver == true) {
				bool proceed = FolderClearing.ClearWithChoice(folder, new string[] { CheckpointInspector.SplitFolder },
					"Start over discards split\\ - your edits - and splits extract\\ afresh.", "split");
				if (proceed == false) {
					problem = "Cancelled. Nothing was changed.";
				}
			}
			if (problem.Length == 0 && state.Form == CheckpointForm.Packed) {
				problem = "Nothing to split: extract\\ holds no sources. Extract first.";
			}
			if (problem.Length == 0 && state.Engine == CheckpointEngine.Siglus) {
				string extract = Path.Combine(folder, CheckpointInspector.ExtractFolder);
				string split = Path.Combine(folder, CheckpointInspector.SplitFolder);
				List<string> warnings = new();
				onLine("Splitting the scenes...");
				CheckpointWatch.Ignoring = true;
				problem = SiglusSplit.SplitFolder(extract, split, Glossary.Characters(folder), warnings, onLine, SplitGrain.WholeScript);
				CheckpointWatch.Ignoring = false;
				foreach (string warning in warnings) {
					onLine("Warning: " + warning);
					CheckpointLog.Warning(folder, "Split", warning);
				}
				if (problem.Length == 0) {
					CheckpointLog.Warning(folder, "Split", "split the scenes into split\\ (" + SplitGrain.Describe() + ")");
					RecordGrain(folder);
				}
			}

			if (problem.Length == 0 && state.Engine == CheckpointEngine.NScripter) {
				string script = Path.Combine(folder, CheckpointInspector.ExtractFolder, NScriptArchive.ScriptName);
				string split = Path.Combine(folder, CheckpointInspector.SplitFolder);
				onLine("Splitting " + NScriptArchive.ScriptName + "...");
				CheckpointWatch.Ignoring = true;
				problem = NScripterSplit.Split(script, split, onLine, SplitGrain.WholeScript);
				CheckpointWatch.Ignoring = false;
				if (problem.Length == 0) {
					CheckpointLog.Warning(folder, "Split", "split " + NScriptArchive.ScriptName + " into split\\ (" + SplitGrain.Describe() + ")");
					RecordGrain(folder);
				}
			}
			return problem;
		}


		/// <summary>
		/// Writes the grain the split was cut at into the checkpoint's info, so a later look
		/// says how it was cut.
		/// </summary>
		private static void RecordGrain(string folder) {
			CheckpointInfo info = CheckpointInfo.Load(folder);
			info.SplitMode = SplitGrain.Word;
			string problem = info.Save(folder);
			if (problem.Length > 0) {
				CheckpointLog.Warning(folder, "Split", "could not record the split grain: " + problem);
			}
		}
	}
}
