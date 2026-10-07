// File: FolderClearing.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Empties one or more of a checkpoint's folders after asking: back them up into
	/// backups\ first, discard them, or cancel. The folders themselves stay - a watcher sits
	/// on them, and a watched folder that is deleted or moved hangs in Windows' delete-
	/// pending state, refusing everyone - so only what is inside goes, with the watcher
	/// told to ignore the burst.
	/// </summary>
	public static class FolderClearing {

		/// <summary>
		/// Asks, then empties the named folders under a checkpoint. A folder that is absent
		/// or already empty is skipped without comment.
		/// </summary>
		/// <param name="checkpointFolder">The checkpoint's folder.</param>
		/// <param name="names">The subfolders to empty, e.g. "split" and "extract".</param>
		/// <param name="prompt">What the user is told before choosing.</param>
		/// <param name="backupPrefix">The backup folder's name starts with this, then a stamp.</param>
		/// <returns>True to go on; false when the user cancelled (said nothing) or the backup failed (said so).</returns>
		public static bool ClearWithChoice(string checkpointFolder, string[] names, string prompt, string backupPrefix) {
			bool proceed = true;
			List<string> populated = new();
			foreach (string name in names) {
				string folder = Path.Combine(checkpointFolder, name);
				if (Directory.Exists(folder) == true && Directory.EnumerateFileSystemEntries(folder).GetEnumerator().MoveNext() == true) {
					populated.Add(name);
				}
			}
			if (populated.Count > 0) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText(prompt);
				menu.AddChoice(new ConsoleMenuItem("Back up to backups\\ first, then go on"));
				menu.AddChoice(new ConsoleMenuItem("Discard"));
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				int choice = menu.GetChoice();
				if (choice == 0) {
					string backup = Path.Combine(checkpointFolder, CheckpointInspector.BackupsFolder, backupPrefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
					CheckpointWatch.Ignoring = true;
					try {
						foreach (string name in populated) {
							string target = Path.Combine(backup, name);
							Directory.CreateDirectory(target);
							MoveContents(Path.Combine(checkpointFolder, name), target);
						}
						Console.WriteLine("Backed up to " + backup);
					}
					catch (Exception exception) {
						Console.WriteLine("Could not back up: " + exception.Message + " What was moved so far is in " + backup);
						proceed = false;
					}
					CheckpointWatch.Ignoring = false;
				}
				if (choice == 1) {
					CheckpointWatch.Ignoring = true;
					try {
						foreach (string name in populated) {
							DeleteContents(Path.Combine(checkpointFolder, name));
						}
					}
					catch (Exception exception) {
						Console.WriteLine("Could not clear: " + exception.Message);
						proceed = false;
					}
					CheckpointWatch.Ignoring = false;
				}
				if (choice != 0 && choice != 1) {
					proceed = false;
				}
			}
			return proceed;
		}


		/// <summary>
		/// Copies the named folders' contents into backups\&lt;prefix&gt;_&lt;stamp&gt;\&lt;name&gt; without
		/// asking and without touching the originals: the affirmative backup Join and Build
		/// take when their switch is on. A folder that is absent or empty is skipped.
		/// </summary>
		/// <param name="checkpointFolder">The checkpoint's folder.</param>
		/// <param name="names">The subfolders to copy, e.g. "extract".</param>
		/// <param name="backupPrefix">The backup folder's name starts with this, then a stamp.</param>
		/// <param name="onLine">Told where the copy went.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string BackUpContents(string checkpointFolder, string[] names, string backupPrefix, Action<string> onLine) {
			string problem = "";
			string backup = Path.Combine(checkpointFolder, CheckpointInspector.BackupsFolder, backupPrefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
			int copied = 0;
			try {
				foreach (string name in names) {
					string folder = Path.Combine(checkpointFolder, name);
					if (Directory.Exists(folder) == true && Directory.EnumerateFileSystemEntries(folder).GetEnumerator().MoveNext() == true) {
						CopyTree(folder, Path.Combine(backup, name));
						copied += 1;
					}
				}
				if (copied > 0) {
					onLine("Backed up to " + backup);
				}
			}
			catch (Exception exception) {
				problem = "Could not back up: " + exception.Message + " Nothing was changed.";
			}
			return problem;
		}


		/// <summary>
		/// Copies a folder and everything under it.
		/// </summary>
		public static void CopyTree(string from, string to) {
			Directory.CreateDirectory(to);
			foreach (string file in Directory.GetFiles(from)) {
				File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
			}
			foreach (string directory in Directory.GetDirectories(from)) {
				CopyTree(directory, Path.Combine(to, Path.GetFileName(directory)));
			}
		}


		/// <summary>
		/// Moves every file and folder inside one folder into another, leaving the first
		/// folder itself in place.
		/// </summary>
		public static void MoveContents(string from, string to) {
			foreach (string file in Directory.GetFiles(from)) {
				File.Move(file, Path.Combine(to, Path.GetFileName(file)));
			}
			foreach (string directory in Directory.GetDirectories(from)) {
				Directory.Move(directory, Path.Combine(to, Path.GetFileName(directory)));
			}
		}


		/// <summary>
		/// Deletes every file and folder inside a folder, leaving the folder itself in place.
		/// </summary>
		public static void DeleteContents(string folder) {
			foreach (string file in Directory.GetFiles(folder)) {
				File.Delete(file);
			}
			foreach (string directory in Directory.GetDirectories(folder)) {
				Directory.Delete(directory, true);
			}
		}
	}
}
