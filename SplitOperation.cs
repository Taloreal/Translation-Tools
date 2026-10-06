// File: SplitOperation.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Split: extract\ -> split\. Runs only on an unsplit checkpoint; a split one keeps
	/// its split, since splitting again would orphan the edits in it. NScripter goes
	/// through NScripterSplit. Siglus is not built yet.
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
			}
			CheckpointWatch.MarkStale();
			ConsoleExt.WaitForEnter("continue");
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
				problem = "Siglus split is not built yet.";
			}

			if (problem.Length == 0 && state.Engine == CheckpointEngine.NScripter) {
				string script = Path.Combine(folder, CheckpointInspector.ExtractFolder, NScriptArchive.ScriptName);
				string split = Path.Combine(folder, CheckpointInspector.SplitFolder);
				onLine("Splitting " + NScriptArchive.ScriptName + "...");
				CheckpointWatch.Ignoring = true;
				problem = NScripterSplit.Split(script, split, onLine);
				CheckpointWatch.Ignoring = false;
				if (problem.Length == 0) {
					CheckpointLog.Warning(folder, "Split", "split " + NScriptArchive.ScriptName + " into split\\");
				}
			}
			return problem;
		}
	}
}
