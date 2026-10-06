// File: RecoverOperation.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Recover: puts back a rebuildable piece of an NScripter checkpoint. nscript.dat is
	/// rebuilt from extract\0.txt; 0.txt is rebuilt from a complete split by Join, which is
	/// not built yet. A successful recovery unlocks the checkpoint again, since the lock was
	/// only ever there to make recovery come first.
	/// </summary>
	public static class RecoverOperation {

		/// <summary>
		/// Runs Recover on the selected checkpoint, printing as it goes, and pauses at the end.
		/// </summary>
		public static void Run() {
			Checkpoint? checkpoint = CheckpointList.Selected();
			string problem = "No checkpoint is selected.";
			if (checkpoint != null) {
				problem = Recover(checkpoint);
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
			}
			if (problem.Length == 0) {
				Console.WriteLine("Recovered and unlocked.");
			}
			CheckpointWatch.MarkStale();
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// Rebuilds whatever is missing and unlocks the checkpoint. Printing-free, so it can
		/// be exercised without a console.
		/// </summary>
		/// <param name="checkpoint">The checkpoint to recover.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Recover(Checkpoint checkpoint) {
			string problem = "";
			string folder = CheckpointInspector.FolderOf(checkpoint.Path);
			CheckpointState state = CheckpointInspector.Inspect(checkpoint.Path);

			if (state.Form == CheckpointForm.Invalid) {
				problem = "\"" + checkpoint.Label + "\" is invalid: " + state.Reason + ". There is nothing to recover from.";
			}
			if (problem.Length == 0 && state.NeedsRecovery == false) {
				problem = "\"" + checkpoint.Label + "\" has nothing to recover.";
			}

			if (problem.Length == 0) {
				string master = Path.Combine(folder, NScriptArchive.ArchiveName);
				string script = Path.Combine(folder, CheckpointInspector.ExtractFolder, NScriptArchive.ScriptName);
				bool masterMissing = File.Exists(master) == false;
				bool scriptMissing = File.Exists(script) == false;

				if (scriptMissing == true) {
					problem = "0.txt has to be rebuilt from the split by Join, which is not built yet.";
				}
				if (problem.Length == 0 && masterMissing == true) {
					problem = NScriptArchive.EncodeFile(script, master);
					if (problem.Length == 0) {
						CheckpointLog.Warning(folder, "Recovery", "rebuilt " + NScriptArchive.ArchiveName + " from " + NScriptArchive.ScriptName);
					}
				}
			}

			if (problem.Length == 0) {
				CheckpointState after = CheckpointInspector.Inspect(checkpoint.Path);
				if (after.NeedsRecovery == true) {
					problem = "Still needs recovery after rebuilding: " + after.Warning;
				}
				if (after.NeedsRecovery == false) {
					Checkpoint updated = checkpoint;
					updated.Writable = true;
					CheckpointList.Update(checkpoint.Label, updated);
					CheckpointLog.Warning(folder, "Recovery", "recovered; unlocked");
				}
			}

			// "Nothing to recover" is an answer, not a failure; it is not history.
			if (problem.Length > 0 && problem.EndsWith("has nothing to recover.") == false) {
				CheckpointLog.Error(folder, "Recovery", problem);
			}
			return problem;
		}
	}
}
