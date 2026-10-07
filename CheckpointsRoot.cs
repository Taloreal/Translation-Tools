// File: CheckpointsRoot.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Where the tool makes checkpoint folders of its own - when Add turns a game folder into
	/// a checkpoint, for one. Each lands in &lt;root&gt;\&lt;label&gt;\, so a label that cannot
	/// be a folder name is refused. Set once in Settings; asked for the first time it is needed.
	/// </summary>
	public static class CheckpointsRoot {

		private const string RootKey = "Checkpoints.Root";

		/// <summary>The root folder, or empty when it has not been set.</summary>
		public static string Folder {
			get {
				Settings.GetValue(RootKey, out string? root);
				return (root ?? "").Trim();
			}
			set { Settings.SetValue(RootKey, value.Trim().TrimEnd('\\')); }
		}

		/// <summary>Whether a root has been set.</summary>
		public static bool IsSet {
			get { return Folder.Length > 0; }
		}


		/// <summary>
		/// Asks for the root when none is set, making the folder. Called by whatever first
		/// needs the root: Add, Fork, Align.
		/// </summary>
		/// <param name="why">One line saying what will go under the root.</param>
		/// <param name="problem">Why the folder could not be used, when it could not.</param>
		/// <returns>True when a root is set afterwards; false when cancelled or problem says why.</returns>
		public static bool Ask(string why, out string problem) {
			problem = "";
			if (IsSet == false) {
				Console.WriteLine(why);
				string root = ConsoleExt.ReadLine("Checkpoints folder (blank to cancel): ", -1, false).Trim().Trim('"');
				if (root.Length > 0) {
					try {
						Directory.CreateDirectory(root);
						Folder = Path.GetFullPath(root);
					}
					catch (Exception exception) {
						problem = "Could not use that folder: " + exception.Message;
					}
				}
			}
			return IsSet;
		}


		/// <summary>
		/// Whether a label can be a folder name under the root.
		/// </summary>
		/// <param name="label">The label to check.</param>
		/// <param name="problem">Why not, when it cannot.</param>
		/// <returns>True when the label is usable as a folder name.</returns>
		public static bool IsFolderSafe(string label, out string problem) {
			problem = "";
			string bad = "";
			foreach (char forbidden in Path.GetInvalidFileNameChars()) {
				if (label.Contains(forbidden) == true && bad.Contains(forbidden) == false && forbidden >= ' ') {
					bad += forbidden;
				}
			}
			if (bad.Length > 0) {
				problem = "The label cannot be a folder name; it holds " + bad;
			}
			if (problem.Length == 0 && (label.Trim().Length == 0 || label.Trim().TrimEnd('.').Length == 0)) {
				problem = "The label cannot be a folder name.";
			}
			return problem.Length == 0;
		}


		/// <summary>
		/// The folder a new checkpoint with a label would get: &lt;root&gt;\&lt;label&gt;. Empty when
		/// no root is set or the label is not folder-safe; problem then says which.
		/// </summary>
		/// <param name="label">The new checkpoint's label.</param>
		/// <param name="problem">Why no folder could be named, when it could not.</param>
		/// <returns>The folder path, or empty.</returns>
		public static string FolderFor(string label, out string problem) {
			string folder = "";
			problem = "";
			if (IsSet == false) {
				problem = "No checkpoints folder is set.";
			}
			if (problem.Length == 0 && IsFolderSafe(label, out problem) == true) {
				folder = Path.Combine(Folder, label.Trim());
			}
			return folder;
		}
	}
}
