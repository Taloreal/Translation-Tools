// File: CheckpointInfo.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// What is true of a checkpoint wherever its folder goes: the game it is, as the user
	/// confirmed it, and VNDB's id for that game. Kept as checkpoint.info at the top of the
	/// folder, one named line per field, so a hand-copied or re-pointed checkpoint still
	/// knows its game and Extract need not ask again. The build mode is deliberately NOT
	/// here: it belongs to the archive that was extracted, and is found anew each time.
	/// The inspector ignores the file.
	/// </summary>
	public class CheckpointInfo {

		/// <summary>The file's name, allowed at the top level of every checkpoint.</summary>
		public const string FileName = "checkpoint.info";

		private const string GameNameName = "GameName";
		private const string VndbIdName = "VndbId";
		private const string BuiltWithName = "BuiltWith";


		/// <summary>
		/// Reads the file at the top of a checkpoint's folder. A folder with no file, or one
		/// that cannot be read, gives empty fields.
		/// </summary>
		/// <param name="folder">The checkpoint's folder.</param>
		/// <returns>The info, never null.</returns>
		public static CheckpointInfo Load(string folder) {
			CheckpointInfo info = new();
			string path = Path.Combine(folder, FileName);
			if (folder.Length > 0 && File.Exists(path) == true) {
				string[] lines = new string[0];
				try {
					lines = File.ReadAllLines(path);
				}
				catch (Exception) {
					// Unreadable is the same as absent: Extract asks again.
				}
				foreach (string line in lines) {
					int equals = line.IndexOf('=');
					if (equals > 0) {
						string name = line.Substring(0, equals);
						string value = line.Substring(equals + 1).Trim();
						if (name == GameNameName) {
							info.GameName = value;
						}
						if (name == VndbIdName) {
							info.VndbId = value;
						}
						if (name == BuiltWithName) {
							bool parsed = int.TryParse(value, out int mode);
							info.BuiltWith = parsed == true
								? mode
								: Checkpoint.NoBuildMode;
						}
					}
				}
			}
			return info;
		}


		/// <summary>
		/// Reads the file only for a checkpoint that is not invalid. The inspector deletes
		/// the file on finding a checkpoint invalid; this guards the moment in between.
		/// </summary>
		/// <param name="folder">The checkpoint's folder.</param>
		/// <param name="state">The checkpoint's state, as the inspector found it.</param>
		/// <returns>The info, or empty fields for an invalid checkpoint; never null.</returns>
		public static CheckpointInfo Load(string folder, CheckpointState state) {
			CheckpointInfo info = new();
			if (state.Form != CheckpointForm.Invalid) {
				info = Load(folder);
			}
			return info;
		}


		/// <summary>
		/// Deletes the file, if there is one. Called by the inspector when it finds the
		/// checkpoint invalid: what the info said is no longer known to be true of the folder.
		/// A file that cannot be deleted is left; the invalid state hides it anyway.
		/// </summary>
		/// <param name="folder">The checkpoint's folder.</param>
		public static void Discard(string folder) {
			string path = Path.Combine(folder, FileName);
			if (File.Exists(path) == true) {
				try {
					File.Delete(path);
				}
				catch (Exception) {
					// Left in place; Load(folder, state) reads an invalid checkpoint as empty.
				}
			}
		}


		/// <summary>The game, as confirmed or typed at extraction. Empty until then, and always empty for NScripter.</summary>
		public string GameName = "";

		/// <summary>VNDB's id for the game, e.g. "v751", once the name has been canonized. Empty until then.</summary>
		public string VndbId = "";

		/// <summary>
		/// Siglus: the build mode the master in this folder was last BUILT with, written by
		/// Build. A fact about that file, not the extract-time mode: Recover uses it only when
		/// the checkpoint records no mode, and says so. NoBuildMode until a build has happened.
		/// </summary>
		public int BuiltWith = Checkpoint.NoBuildMode;


		/// <summary>
		/// Writes the file at the top of a checkpoint's folder, replacing what was there.
		/// </summary>
		/// <param name="folder">The checkpoint's folder.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public string Save(string folder) {
			string problem = "";
			try {
				string text = GameNameName + "=" + GameName + Environment.NewLine
					+ VndbIdName + "=" + VndbId + Environment.NewLine
					+ BuiltWithName + "=" + BuiltWith + Environment.NewLine;
				File.WriteAllText(Path.Combine(folder, FileName), text);
			}
			catch (Exception exception) {
				problem = "Could not write " + FileName + ": " + exception.Message;
			}
			return problem;
		}
	}
}
