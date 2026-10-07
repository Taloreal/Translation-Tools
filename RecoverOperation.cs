// File: RecoverOperation.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Recover: puts back a rebuildable piece of a checkpoint. NScripter: nscript.dat is
	/// rebuilt from extract\0.txt, and 0.txt from a complete split by Join. Siglus: extract\
	/// is rebuilt from a complete split by Join, support files included, and Scene.pck is
	/// compiled from extract\ - a rebuild, never the original bytes, under the checkpoint's
	/// recorded mode, else the mode the last master was built with, else the standard mode,
	/// each fallback a warning and not a stop. A successful recovery unlocks the checkpoint
	/// again, since the lock was only ever there to make recovery come first.
	/// </summary>
	public static class RecoverOperation {

		/// <summary>True while a recovery rebuilt the master, so the record it writes counts as a build.</summary>
		private static bool MasterRebuilt = false;

		/// <summary>
		/// Runs Recover on the selected checkpoint, printing as it goes, and pauses at the end.
		/// </summary>
		public static void Run() {
			Checkpoint? checkpoint = CheckpointList.Selected();
			string problem = "No checkpoint is selected.";
			if (checkpoint != null) {
				problem = Recover(checkpoint, Console.WriteLine);
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
		/// Rebuilds whatever is missing and unlocks the checkpoint.
		/// </summary>
		/// <param name="checkpoint">The checkpoint to recover.</param>
		/// <param name="onLine">Receives progress and the compiler's output.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Recover(Checkpoint checkpoint, Action<string> onLine) {
			string problem = "";
			string folder = CheckpointInspector.FolderOf(checkpoint.Path);
			CheckpointState state = CheckpointInspector.Inspect(checkpoint.Path);

			if (state.Form == CheckpointForm.Invalid) {
				problem = "\"" + checkpoint.Label + "\" is invalid: " + state.Reason + ". There is nothing to recover from.";
			}
			if (problem.Length == 0 && state.NeedsRecovery == false) {
				problem = "\"" + checkpoint.Label + "\" has nothing to recover.";
			}

			MasterRebuilt = false;
			if (problem.Length == 0 && state.Engine == CheckpointEngine.NScripter) {
				problem = RecoverNscripter(folder, onLine);
			}
			if (problem.Length == 0 && state.Engine == CheckpointEngine.Siglus) {
				problem = RecoverSiglus(checkpoint, folder, onLine);
			}

			if (problem.Length == 0) {
				SourceHashes.Write(folder, MasterRebuilt);
				CheckpointState after = CheckpointInspector.Inspect(checkpoint.Path);
				if (after.NeedsRecovery == true) {
					problem = "Still needs recovery after rebuilding: " + after.Warning;
				}
				if (after.NeedsRecovery == false) {
					checkpoint.Writable = true;
					CheckpointList.Update(checkpoint.Label, checkpoint);
					CheckpointLog.Warning(folder, "Recovery", "recovered; unlocked");
				}
			}

			// "Nothing to recover" is an answer, not a failure; it is not history.
			if (problem.Length > 0 && problem.EndsWith("has nothing to recover.") == false) {
				CheckpointLog.Error(folder, "Recovery", problem);
			}
			return problem;
		}


		/// <summary>
		/// NScripter: 0.txt from the split if it is missing, then nscript.dat from 0.txt.
		/// </summary>
		private static string RecoverNscripter(string folder, Action<string> onLine) {
			string problem = "";
			string master = Path.Combine(folder, NScriptArchive.ArchiveName);
			string script = Path.Combine(folder, CheckpointInspector.ExtractFolder, NScriptArchive.ScriptName);
			if (File.Exists(script) == false) {
				// The inspector only flags this when the split is complete.
				problem = JoinOperation.JoinNscripter(folder, onLine);
				if (problem.Length == 0) {
					CheckpointLog.Warning(folder, "Recovery", "rebuilt " + NScriptArchive.ScriptName + " from the split");
				}
			}
			if (problem.Length == 0 && File.Exists(master) == false) {
				problem = NScriptArchive.EncodeFile(script, master);
				if (problem.Length == 0) {
					MasterRebuilt = true;
					CheckpointLog.Warning(folder, "Recovery", "rebuilt " + NScriptArchive.ArchiveName + " from " + NScriptArchive.ScriptName);
				}
			}
			return problem;
		}


		/// <summary>
		/// Siglus: extract\ from the split if it has no sources, then Scene.pck from extract\.
		/// The mode comes from the checkpoint, else from checkpoint.info's record of the last
		/// build, else the standard mode; the last two are warnings.
		/// </summary>
		private static string RecoverSiglus(Checkpoint checkpoint, string folder, Action<string> onLine) {
			string problem = "";
			string master = Path.Combine(folder, "Scene.pck");
			string extract = Path.Combine(folder, CheckpointInspector.ExtractFolder);
			bool hasSources = Directory.Exists(extract) == true && Directory.GetFiles(extract, "*" + SiglusScript.ScriptExtension).Length > 0;
			if (hasSources == false) {
				problem = JoinOperation.JoinSiglus(checkpoint, folder, onLine);
				if (problem.Length == 0) {
					CheckpointLog.Warning(folder, "Recovery", "rebuilt extract\\ from the split");
				}
			}
			if (problem.Length == 0 && File.Exists(master) == false) {
				if (checkpoint.HasBuildMode == false) {
					CheckpointInfo info = CheckpointInfo.Load(folder);
					if (info.BuiltWith != Checkpoint.NoBuildMode) {
						checkpoint.BuildMode = info.BuiltWith;
						CheckpointList.Update(checkpoint.Label, checkpoint);
						string warning = "no verified build mode; using the mode the last master was built with (" + info.BuiltWith + ")";
						onLine("Warning: " + warning);
						CheckpointLog.Warning(folder, "Recovery", warning);
					}
				}
				if (checkpoint.HasBuildMode == false) {
					string warning = "no build mode known; the rebuilt master uses the standard mode and may not run";
					onLine("Warning: " + warning);
					CheckpointLog.Warning(folder, "Recovery", warning);
				}
				problem = BuildOperation.BuildSiglus(checkpoint, folder, false, onLine);
				if (problem.Length == 0) {
					MasterRebuilt = true;
					CheckpointLog.Warning(folder, "Recovery", "rebuilt Scene.pck from extract\\ - a rebuild, not the original");
				}
			}
			return problem;
		}
	}
}
