// File: CheckpointsMenu.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Manages the checkpoint list: add, rename, re-point, lock or unlock, remove. Opened
	/// from the selector on the top menu, and acts on whichever checkpoint is selected.
	/// </summary>
	public static class CheckpointsMenu {

		private static readonly ConsoleMenuItem LockItem = new("Lock this checkpoint");

		/// <summary>Every row's on-disk state, probed once when the menu opens and after an action changes the list.</summary>
		private static readonly Dictionary<string, CheckpointState> RowStates = new(StringComparer.OrdinalIgnoreCase);


		/// <summary>
		/// Shows the menu until the user chooses Back.
		/// </summary>
		public static void Show() {
			ProbeAllRows();
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			menu.AddChoice(new ConsoleMenuItem("Add a checkpoint").SetActionOnSelect(AddCheckpoint));
			menu.AddChoice(new ConsoleMenuItem("Rename this label").SetActionOnSelect(RenameLabel));
			menu.AddChoice(new ConsoleMenuItem("Re-point this path").SetActionOnSelect(RepointPath));
			menu.AddChoice(LockItem.SetActionOnSelect(ToggleLock));
			menu.AddChoice(new ConsoleMenuItem("Remove this checkpoint").SetActionOnSelect(RemoveCheckpoint));
			menu.AddChoice(new ConsoleMenuItem("Fork this checkpoint").SetActionOnSelect(ForkCheckpoint));
			menu.AddChoice(new ConsoleMenuItem("Open this checkpoint in Explorer").SetActionOnSelect(OpenInExplorer));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Copies the selected checkpoint into a new one under the checkpoints folder: the
		/// master, extract\, split\, and the info, hashes and log files, but not backups\.
		/// The copy is writable, selected, and carries the source's recorded build mode,
		/// compiler version, warning and game install. The source is not changed, so a
		/// locked or invalid one forks too. Both logs record the fork.
		/// </summary>
		private static void ForkCheckpoint() {
			Checkpoint? source = CheckpointList.Selected();
			string problem = "";
			if (source == null) {
				problem = "No checkpoint is selected.";
			}
			string sourceFolder = "";
			if (problem.Length == 0) {
				sourceFolder = CheckpointInspector.FolderOf(source!.Path);
				if (sourceFolder.Length == 0 || Directory.Exists(sourceFolder) == false) {
					problem = "The folder is missing: " + source.Path;
				}
			}
			string label = "";
			if (problem.Length == 0) {
				label = ConsoleExt.ReadLine("Label for the fork of \"" + source!.Label + "\" (blank to cancel): ", -1, false).Trim();
				if (label.Length == 0) {
					problem = "Cancelled. Nothing forked.";
				}
			}
			if (problem.Length == 0 && Checkpoint.IsStorable(label) == false) {
				problem = "That label cannot be stored.";
			}
			if (problem.Length == 0 && CheckpointList.Find(label) != null) {
				problem = "A checkpoint is already labelled \"" + label + "\".";
			}
			string destination = "";
			if (problem.Length == 0) {
				destination = CheckpointsRoot.FolderFor(label, out string rootProblem);
				if (destination.Length == 0) {
					problem = rootProblem;
				}
			}
			if (problem.Length == 0 && Directory.Exists(destination) == true && Directory.EnumerateFileSystemEntries(destination).GetEnumerator().MoveNext() == true) {
				problem = destination + " already has files in it.";
			}
			if (problem.Length == 0) {
				try {
					CopyCheckpointContents(sourceFolder, destination);
					Checkpoint fork = new();
					fork.Label = label;
					fork.Path = Path.GetFullPath(destination).TrimEnd('\\');
					fork.Writable = true;
					fork.BuildMode = source!.BuildMode;
					fork.CompilerVersion = source.CompilerVersion;
					fork.Warning = source.Warning;
					fork.GameFolder = source.GameFolder;
					CheckpointList.Add(fork);
					CheckpointLog.Warning(sourceFolder, "Fork", "forked to \"" + label + "\" at " + fork.Path);
					CheckpointLog.Warning(destination, "Fork", "forked from \"" + source.Label + "\" at " + sourceFolder);
					ProbeAllRows();
					Console.WriteLine("Forked to \"" + label + "\" at " + fork.Path + " and selected it (writable).");
				}
				catch (Exception exception) {
					problem = "Could not fork: " + exception.Message + " What was copied so far is at " + destination;
				}
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
			}
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// Copies what makes a checkpoint - the top-level files and the extract\ and split\
		/// trees - into a new folder. backups\ stays behind.
		/// </summary>
		private static void CopyCheckpointContents(string sourceFolder, string destination) {
			Directory.CreateDirectory(destination);
			foreach (string file in Directory.GetFiles(sourceFolder)) {
				File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
			}
			string[] trees = new string[] { CheckpointInspector.ExtractFolder, CheckpointInspector.SplitFolder };
			foreach (string tree in trees) {
				string from = Path.Combine(sourceFolder, tree);
				if (Directory.Exists(from) == true) {
					CopyTree(from, Path.Combine(destination, tree));
				}
			}
		}


		/// <summary>
		/// Copies a folder and everything under it.
		/// </summary>
		private static void CopyTree(string from, string to) {
			Directory.CreateDirectory(to);
			foreach (string file in Directory.GetFiles(from)) {
				File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
			}
			foreach (string directory in Directory.GetDirectories(from)) {
				CopyTree(directory, Path.Combine(to, Path.GetFileName(directory)));
			}
		}


		/// <summary>
		/// Opens the selected checkpoint's folder in Windows Explorer. A path whose folder is
		/// gone is said so, not opened.
		/// </summary>
		private static void OpenInExplorer() {
			Checkpoint? selected = CheckpointList.Selected();
			string problem = "No checkpoint is selected.";
			if (selected != null) {
				string folder = CheckpointInspector.FolderOf(selected.Path);
				problem = "The folder is missing: " + selected.Path;
				if (folder.Length > 0 && Directory.Exists(folder) == true) {
					problem = "";
					try {
						System.Diagnostics.ProcessStartInfo start = new("explorer.exe", "\"" + folder + "\"");
						start.UseShellExecute = true;
						System.Diagnostics.Process.Start(start);
					}
					catch (Exception exception) {
						problem = "Could not open Explorer: " + exception.Message;
					}
				}
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				ConsoleExt.WaitForEnter("continue");
			}
		}


		/// <summary>
		/// Writes the selected checkpoint and the list size above the choices, and names the
		/// lock item for the action that applies.
		/// </summary>
		private static void RefreshHeader(ConsoleSelectMenu menu) {
			Checkpoint? selected = CheckpointList.Selected();
			CheckpointWatch.Focus(selected);
			List<Checkpoint> all = CheckpointList.All();
			string header = "-- Checkpoints (" + all.Count + ") --\n";
			foreach (Checkpoint row in all) {
				bool isSelected = selected != null && CheckpointList.SameLabel(row.Label, selected.Label) == true;
				CheckpointState state = RowState(row, isSelected);
				string marker = "  ";
				if (isSelected == true) {
					marker = "> ";
				}
				string game = "";
				CheckpointInfo info = CheckpointInfo.Load(CheckpointInspector.FolderOf(row.Path), state);
				if (info.GameName.Length > 0) {
					game = "  " + info.GameName;
				}
				if (row.Warning.Length > 0) {
					game += "  WARNING: " + row.Warning;
				}
				header += marker + row.Label.PadRight(20) + "  " + state.Describe().PadRight(26) + "  " + row.StateWord + game + "\n";
			}
			if (selected == null) {
				header += "No checkpoints yet. Add one to begin.\n";
				LockItem.SetText("Lock this checkpoint");
			}
			if (selected != null) {
				header += "Path: " + selected.Path + "\n";
				if (selected.Writable == true) {
					LockItem.SetText("Lock this checkpoint");
				}
				if (selected.Writable == false) {
					LockItem.SetText("Unlock this checkpoint");
				}
			}
			menu.SetPreChoiceText(header);
		}


		/// <summary>
		/// Asks for a label and a path, refuses a taken label or a path that does not exist,
		/// and selects the new checkpoint. New checkpoints start writable; locking one is a
		/// deliberate step.
		/// </summary>
		private static void AddCheckpoint() {
			string label = ConsoleExt.ReadLine("Label for the new checkpoint (blank to cancel): ", -1, false).Trim();
			if (label.Length > 0) {
				string problem = "";
				if (Checkpoint.IsStorable(label) == false) {
					problem = "That label cannot be stored.";
				}
				if (problem.Length == 0 && CheckpointList.Find(label) != null) {
					problem = "A checkpoint is already labelled \"" + label + "\".";
				}
				if (problem.Length == 0) {
					string path = ConsoleExt.ReadLine("Folder or archive it points at: ", -1, false).Trim().Trim('"');
					if (PathExists(path) == false) {
						problem = "Nothing exists at " + path;
					}
					if (problem.Length == 0) {
						Checkpoint checkpoint = new();
						checkpoint.Label = label;
						checkpoint.Path = path;
						checkpoint.Writable = true;
						bool cancelled = false;
						problem = OfferGameFolderCopy(checkpoint, out cancelled);
						if (problem.Length == 0 && cancelled == false) {
							CheckpointList.Add(checkpoint);
							ProbeAllRows();
							Console.WriteLine("Added and selected \"" + label + "\" (writable).");
						}
						if (cancelled == true) {
							Console.WriteLine("Cancelled. Nothing added.");
						}
					}
				}
				if (problem.Length > 0) {
					Console.WriteLine(problem + " Nothing added.");
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}


		/// <summary>
		/// Gives the selected checkpoint a new label; blank keeps the current one.
		/// </summary>
		private static void RenameLabel() {
			Checkpoint? selected = CheckpointList.Selected();
			if (selected == null) {
				Console.WriteLine("No checkpoint is selected.");
			}
			if (selected != null) {
				Console.WriteLine("Current: " + selected.Label);
				string label = ConsoleExt.ReadLine("New label (blank keeps it): ", -1, false).Trim();
				if (label.Length > 0) {
					Checkpoint updated = selected;
					string oldLabel = selected.Label;
					updated.Label = label;
					bool renamed = CheckpointList.Update(oldLabel, updated);
					ProbeAllRows();
					if (renamed == true) {
						Console.WriteLine("Renamed to \"" + label + "\".");
					}
					if (renamed == false) {
						Console.WriteLine("That label is taken or cannot be stored. Nothing changed.");
					}
				}
			}
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// Points the selected checkpoint at a different folder or archive; blank keeps the
		/// current path. Everything keyed to the label, stamps included, follows it.
		/// </summary>
		private static void RepointPath() {
			Checkpoint? selected = CheckpointList.Selected();
			if (selected == null) {
				Console.WriteLine("No checkpoint is selected.");
			}
			if (selected != null) {
				Console.WriteLine("Current: " + selected.Path);
				string path = ConsoleExt.ReadLine("New folder or archive (blank keeps it): ", -1, false).Trim().Trim('"');
				if (path.Length > 0) {
					if (PathExists(path) == false) {
						Console.WriteLine("Nothing exists at " + path + ". Nothing changed.");
					}
					if (PathExists(path) == true) {
						Checkpoint updated = selected;
						updated.Path = path;
						CheckpointList.Update(selected.Label, updated);
						ProbeAllRows();
						Console.WriteLine("Now points at " + path);
					}
				}
			}
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// Locks a writable checkpoint or unlocks a locked one.
		/// </summary>
		private static void ToggleLock() {
			Checkpoint? selected = CheckpointList.Selected();
			if (selected == null) {
				Console.WriteLine("No checkpoint is selected.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (selected != null) {
				bool unlocking = selected.Writable == false;
				bool allowed = true;
				if (unlocking == true) {
					allowed = RecoveryGate.MayUnlock(selected, out string reason);
					if (allowed == false) {
						Console.WriteLine(reason);
						ConsoleExt.WaitForEnter("continue");
					}
				}
				if (allowed == true) {
					Checkpoint updated = selected;
					updated.Writable = selected.Writable == false;
					CheckpointList.Update(selected.Label, updated);
				}
			}
		}


		/// <summary>
		/// Removes the selected checkpoint from the list after a yes. The folder on disk is
		/// never touched.
		/// </summary>
		private static void RemoveCheckpoint() {
			Checkpoint? selected = CheckpointList.Selected();
			if (selected == null) {
				Console.WriteLine("No checkpoint is selected.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (selected != null) {
				bool confirmed = ConsoleExt.ReadValue<bool>(
					"Remove \"" + selected.Label + "\" from the list? The folder stays on disk. (y/n): ", false);
				if (confirmed == true) {
					CheckpointList.Remove(selected.Label);
					ProbeAllRows();
					Console.WriteLine("Removed \"" + selected.Label + "\" from the list.");
					ConsoleExt.WaitForEnter("continue");
				}
			}
		}


		/// <summary>
		/// Probes every checkpoint once. The selected one is read through its watcher
		/// afterwards; the rest stay as probed here.
		/// </summary>
		private static void ProbeAllRows() {
			RowStates.Clear();
			foreach (Checkpoint row in CheckpointList.All()) {
				CheckpointState state = CheckpointInspector.Inspect(row.Path);
				RowStates[row.Label] = state;
				RecoveryGate.Enforce(row, state);
			}
		}


		/// <summary>
		/// A row's state: live through the watcher for the selected checkpoint, otherwise the
		/// state probed when the menu opened.
		/// </summary>
		private static CheckpointState RowState(Checkpoint row, bool isSelected) {
			CheckpointState state = CheckpointWatch.State;
			if (isSelected == false) {
				if (RowStates.TryGetValue(row.Label, out CheckpointState? probed) == false) {
					probed = CheckpointInspector.Inspect(row.Path);
					RowStates[row.Label] = probed;
				}
				state = probed;
			}
			return state;
		}


		/// <summary>
		/// When the path given is the installed game itself - not checkpoint-shaped, holding
		/// the engine's start-up files and other things beside the archive - offers to make a
		/// checkpoint from it: a new folder of the user's choosing with a copy of the archive
		/// at its top, and the game folder recorded for Run. The game folder is only read.
		/// </summary>
		/// <param name="checkpoint">The checkpoint being added; its Path and GameFolder may be changed.</param>
		/// <param name="cancelled">True when the user declined to go on at a prompt.</param>
		/// <returns>Empty when fine, otherwise a plain sentence.</returns>
		private static string OfferGameFolderCopy(Checkpoint checkpoint, out bool cancelled) {
			string problem = "";
			cancelled = false;
			CheckpointState state = CheckpointInspector.Inspect(checkpoint.Path);
			GameLocation game = GameLocation.Inspect(checkpoint.Path);
			bool looksLikeGame = state.Form == CheckpointForm.Invalid && game.IsGame == true;
			if (looksLikeGame == true) {
				string gameFolder = Path.GetDirectoryName(game.Archive) ?? "";
				Console.WriteLine("That looks like the installed game's folder (" + Path.GetFileName(game.Archive) + " beside its engine), not a checkpoint.");
				bool makeCopy = ConsoleExt.ReadValue<bool>("Make a checkpoint from it, with a copy of the archive in its own folder? (y/n): ", false);
				if (makeCopy == false) {
					cancelled = true;
				}
				if (makeCopy == true) {
					// New checkpoint folders live under the checkpoints root as <root>\<label>.
					// The root is asked for the first time it is needed and kept in Settings.
					if (CheckpointsRoot.IsSet == false) {
						Console.WriteLine("Checkpoints the tool makes go under one folder, as <folder>\\<label>.");
						string root = ConsoleExt.ReadLine("Checkpoints folder (blank to cancel): ", -1, false).Trim().Trim('"');
						if (root.Length == 0) {
							cancelled = true;
						}
						if (root.Length > 0) {
							try {
								Directory.CreateDirectory(root);
								CheckpointsRoot.Folder = Path.GetFullPath(root);
							}
							catch (Exception exception) {
								problem = "Could not use that folder: " + exception.Message + " Nothing added.";
							}
						}
					}
					string destination = "";
					if (cancelled == false && problem.Length == 0) {
						destination = CheckpointsRoot.FolderFor(checkpoint.Label, out string rootProblem);
						if (destination.Length == 0) {
							problem = rootProblem + " Nothing added.";
						}
					}
					if (cancelled == false && problem.Length == 0 && Directory.Exists(destination) == true && Directory.EnumerateFileSystemEntries(destination).GetEnumerator().MoveNext() == true) {
						problem = destination + " already has files in it. Nothing added.";
					}
					if (cancelled == false && problem.Length == 0) {
						try {
							Directory.CreateDirectory(destination);
							File.Copy(game.Archive, Path.Combine(destination, Path.GetFileName(game.Archive)), false);
							checkpoint.Path = Path.GetFullPath(destination).TrimEnd('\\');
							checkpoint.GameFolder = gameFolder;
							Console.WriteLine("Copied " + Path.GetFileName(game.Archive) + " into " + checkpoint.Path);
						}
						catch (Exception exception) {
							problem = "Could not make the checkpoint folder: " + exception.Message + " Nothing added.";
						}
					}
				}
			}
			return problem;
		}


		private static bool PathExists(string path) {
			return Directory.Exists(path) == true || File.Exists(path) == true;
		}
	}
}
