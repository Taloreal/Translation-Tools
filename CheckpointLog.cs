// File: CheckpointLog.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// A checkpoint's own history of warnings and errors: checkpoint.log at the top of its
	/// folder, one timestamped line per event, only ever appended to. It travels with the
	/// folder, so a hand-copied checkpoint keeps its history. The inspector ignores it.
	/// </summary>
	public static class CheckpointLog {

		/// <summary>The log's file name, allowed at the top level of every checkpoint.</summary>
		public const string FileName = "checkpoint.log";


		/// <summary>
		/// Records a warning: something the operation went ahead with, but the user should know.
		/// </summary>
		/// <param name="folder">The checkpoint's folder.</param>
		/// <param name="operation">Which operation, e.g. "Extract".</param>
		/// <param name="message">What happened, as a plain sentence.</param>
		public static void Warning(string folder, string operation, string message) {
			Append(folder, "WARNING", operation, message);
		}


		/// <summary>
		/// Records an error: something the operation refused or could not finish.
		/// </summary>
		/// <param name="folder">The checkpoint's folder.</param>
		/// <param name="operation">Which operation, e.g. "Extract".</param>
		/// <param name="message">What happened, as a plain sentence.</param>
		public static void Error(string folder, string operation, string message) {
			Append(folder, "ERROR", operation, message);
		}


		/// <summary>
		/// Appends one line. A log that cannot be written (folder gone, disk full) is not
		/// itself an error worth stopping for; the event was already shown on screen.
		/// </summary>
		private static void Append(string folder, string level, string operation, string message) {
			if (folder.Length > 0 && Directory.Exists(folder) == true) {
				try {
					string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + level.PadRight(7) + " " + operation + ": " + message + Environment.NewLine;
					File.AppendAllText(Path.Combine(folder, FileName), line);
				}
				catch (Exception) {
					// Shown on screen already; the file is the history, not the only record.
				}
			}
		}
	}
}
