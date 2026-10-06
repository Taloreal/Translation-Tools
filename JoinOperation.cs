// File: JoinOperation.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Join: split\ -> extract\. Rewrites the sources from the split; the split stays, and
	/// Build then works from extract\. NScripter goes through NScripterJoin, which writes
	/// the script whole into backups\ first and only then moves it over extract\0.txt.
	/// Siglus is not built yet.
	/// </summary>
	public static class JoinOperation {

		/// <summary>
		/// Runs Join on the selected checkpoint, printing as it goes, and pauses at the end.
		/// </summary>
		public static void Run() {
			Checkpoint? checkpoint = CheckpointList.Selected();
			string problem = "No checkpoint is selected.";
			string folder = "";
			if (checkpoint != null) {
				folder = CheckpointInspector.FolderOf(checkpoint.Path);
				problem = Join(checkpoint, Console.WriteLine);
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				CheckpointLog.Error(folder, "Join", problem);
			}
			if (problem.Length == 0) {
				Console.WriteLine("Joined into " + Path.Combine(folder, CheckpointInspector.ExtractFolder));
			}
			CheckpointWatch.MarkStale();
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// Does the join. Printing goes through onLine, so this can be exercised without a
		/// console. Warnings the join went ahead with are printed and logged.
		/// </summary>
		/// <param name="checkpoint">The checkpoint to join.</param>
		/// <param name="onLine">Receives progress and warnings.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Join(Checkpoint checkpoint, Action<string> onLine) {
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
			if (problem.Length == 0 && state.Form != CheckpointForm.Split) {
				problem = "Nothing to join: \"" + checkpoint.Label + "\" has no split.";
			}
			if (problem.Length == 0 && state.Engine == CheckpointEngine.Siglus) {
				problem = "Siglus join is not built yet.";
			}

			if (problem.Length == 0 && state.Engine == CheckpointEngine.NScripter) {
				problem = JoinNscripter(folder, onLine);
			}
			return problem;
		}


		/// <summary>
		/// NScripter: join split\ into extract\0.txt, staging through backups\.
		/// </summary>
		/// <param name="folder">The checkpoint's folder.</param>
		/// <param name="onLine">Receives progress and warnings.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string JoinNscripter(string folder, Action<string> onLine) {
			string split = Path.Combine(folder, CheckpointInspector.SplitFolder);
			string extract = Path.Combine(folder, CheckpointInspector.ExtractFolder);
			string script = Path.Combine(extract, NScriptArchive.ScriptName);
			string staging = Path.Combine(folder, CheckpointInspector.BackupsFolder, NScriptArchive.ScriptName + ".joining_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
			List<string> warnings = new();
			onLine("Joining split\\ into " + NScriptArchive.ScriptName + "...");
			Directory.CreateDirectory(extract);
			CheckpointWatch.Ignoring = true;
			string problem = NScripterJoin.Join(split, script, staging, warnings, onLine);
			CheckpointWatch.Ignoring = false;
			foreach (string warning in warnings) {
				onLine("Warning: " + warning);
				CheckpointLog.Warning(folder, "Join", warning);
			}
			if (problem.Length == 0) {
				CheckpointLog.Warning(folder, "Join", "joined split\\ into " + NScriptArchive.ScriptName);
			}
			return problem;
		}
	}
}
