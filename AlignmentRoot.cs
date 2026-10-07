// File: AlignmentRoot.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// Where alignments live: &lt;checkpoints root&gt;\Alignment\, one folder per pair inside
	/// it. The name is reserved so no checkpoint can ever be made there: Add, Fork and
	/// Rename refuse it as a label, in any case.
	/// </summary>
	public static class AlignmentRoot {

		/// <summary>The folder's name under the checkpoints root, and the label no checkpoint may have.</summary>
		public const string FolderName = "Alignment";

		/// <summary>The Alignment folder, or empty when no checkpoints root is set.</summary>
		public static string Folder {
			get {
				string folder = "";
				if (CheckpointsRoot.IsSet == true) {
					folder = Path.Combine(CheckpointsRoot.Folder, FolderName);
				}
				return folder;
			}
		}


		/// <summary>
		/// Whether a label is the reserved one.
		/// </summary>
		/// <param name="label">The label to check.</param>
		/// <returns>True when a checkpoint may not carry it.</returns>
		public static bool IsReservedLabel(string label) {
			return string.Equals(label.Trim(), FolderName, StringComparison.OrdinalIgnoreCase);
		}


		/// <summary>
		/// Makes sure the Alignment folder exists, asking for the checkpoints root first if
		/// none is set.
		/// </summary>
		/// <param name="problem">Why not, when the folder could not be had; empty otherwise.</param>
		/// <returns>The folder, or empty when problem says why.</returns>
		public static string Ensure(out string problem) {
			problem = "";
			string folder = "";
			bool haveRoot = CheckpointsRoot.IsSet;
			if (haveRoot == false) {
				haveRoot = CheckpointsRoot.Ask("Alignments go under your checkpoints folder, as <folder>\\" + FolderName + "\\.", out problem);
			}
			if (haveRoot == true) {
				folder = Folder;
				try {
					Directory.CreateDirectory(folder);
				}
				catch (Exception exception) {
					problem = "Could not make " + folder + ": " + exception.Message;
					folder = "";
				}
			}
			return folder;
		}
	}
}
