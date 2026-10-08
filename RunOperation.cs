// File: RunOperation.cs
// Namespace: TranslationTools
using System.Diagnostics;
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Run: installs the checkpoint's master into the game and starts the game. The live
	/// archive is backed up into the checkpoint's backups\ first only when the user says so
	/// on the way, since a build loop runs hundreds of times. The checkpoint
	/// points at one of the game installs this machine knows (GameInstallList), which holds
	/// the launcher; Run asks for one the first time, and is gated on that folder holding
	/// the engine's start-up files beside the archive.
	/// </summary>
	public static class RunOperation {

		/// <summary>
		/// Runs on the selected checkpoint, printing as it goes, and pauses at the end.
		/// </summary>
		/// <param name="backUp">Whether the live archive is copied into backups\ before it is overwritten; the menu row's own switch.</param>
		public static void Run(bool backUp) {
			Checkpoint? checkpoint = CheckpointList.Selected();
			string problem = "";
			string folder = "";
			if (checkpoint == null) {
				problem = "No checkpoint is selected.";
			}
			if (problem.Length == 0 && checkpoint!.Writable == false) {
				problem = "\"" + checkpoint.Label + "\" is locked.";
			}

			CheckpointState state = new();
			if (problem.Length == 0) {
				folder = CheckpointInspector.FolderOf(checkpoint!.Path);
				state = CheckpointInspector.Inspect(checkpoint.Path);
				if (state.Form == CheckpointForm.Invalid) {
					problem = "\"" + checkpoint.Label + "\" is invalid: " + state.Reason;
				}
				if (problem.Length == 0 && state.NeedsRecovery == true) {
					problem = "\"" + checkpoint.Label + "\" needs recovery first.";
				}
			}

			string master = "";
			if (problem.Length == 0) {
				master = Path.Combine(folder, MasterName(state.Engine));
				if (File.Exists(master) == false) {
					problem = "The master " + Path.GetFileName(master) + " is not in the checkpoint.";
				}
			}

			GameLocation? game = null;
			GameInstall? install = null;
			if (problem.Length == 0) {
				install = EnsureInstall(checkpoint!, state.Engine, out game, out problem);
			}

			if (problem.Length == 0) {
				// A backup is an affirmative choice made on the menu row, never a side effect:
				// hundreds of build iterations would otherwise pile up copies nobody restores from.
				problem = Install(folder, master, game!, backUp);
			}
			if (problem.Length == 0) {
				problem = Launch(install!.Folder, install.Launcher, install.Arguments);
			}

			if (problem.Length > 0) {
				Console.WriteLine(problem);
				CheckpointLog.Error(folder, "Run", problem);
			}
			if (problem.Length == 0) {
				Console.WriteLine("Installed and launched.");
			}
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// The install the checkpoint points at, or asks for one: a saved install of this
		/// engine, or a new folder, which is inspected, given a launcher, and saved to the
		/// list. The pointer is recorded on the checkpoint either way.
		/// </summary>
		private static GameInstall? EnsureInstall(Checkpoint checkpoint, CheckpointEngine engine, out GameLocation? game, out string problem) {
			problem = "";
			game = null;
			GameInstall? install = null;
			if (checkpoint.GameFolder.Length > 0) {
				install = GameInstallList.Find(checkpoint.GameFolder);
				if (install != null) {
					game = Runnable(install.Folder, engine, out string why);
					if (game == null) {
						Console.WriteLine("The recorded game install cannot be used: " + why);
						install = null;
					}
				}
				if (install != null && File.Exists(install.Launcher) == false) {
					Console.WriteLine("The recorded launcher is gone: " + install.Launcher);
					problem = ChooseLauncher(install, game!);
					if (problem.Length == 0) {
						GameInstallList.Put(install);
					}
				}
			}
			if (problem.Length == 0 && install == null) {
				install = PickInstall(engine, out game, out problem);
			}
			if (problem.Length == 0 && install != null && GameInstall.SameFolder(checkpoint.GameFolder, install.Folder) == false) {
				checkpoint.GameFolder = install.Folder;
				CheckpointList.Update(checkpoint.Label, checkpoint);
			}
			return install;
		}


		/// <summary>
		/// Offers the saved installs that currently run this engine, then "another folder".
		/// </summary>
		private static GameInstall? PickInstall(CheckpointEngine engine, out GameLocation? game, out string problem) {
			problem = "";
			game = null;
			GameInstall? picked = null;
			List<GameInstall> usable = new();
			List<GameLocation> locations = new();
			foreach (GameInstall saved in GameInstallList.All()) {
				GameLocation? location = Runnable(saved.Folder, engine, out string why);
				if (location != null) {
					usable.Add(saved);
					locations.Add(location);
				}
			}
			int choice = usable.Count;
			if (usable.Count > 0) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText("Which installed game should this checkpoint run in?");
				foreach (GameInstall saved in usable) {
					menu.AddChoice(new ConsoleMenuItem(saved.Name + "   " + saved.Folder));
				}
				menu.AddChoice(new ConsoleMenuItem("Another folder..."));
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				choice = menu.GetChoice();
			}
			if (choice >= 0 && choice < usable.Count) {
				picked = usable[choice];
				game = locations[choice];
			}
			if (choice == usable.Count) {
				picked = AddInstall(engine, out game, out problem);
			}
			if (choice > usable.Count || choice < 0) {
				problem = "Cancelled. Nothing was changed.";
			}
			return picked;
		}


		/// <summary>
		/// Asks for a game folder, checks it runs this engine, settles its launcher, and
		/// saves it to the list.
		/// </summary>
		private static GameInstall? AddInstall(CheckpointEngine engine, out GameLocation? game, out string problem) {
			problem = "";
			game = null;
			GameInstall? install = null;
			Console.WriteLine("Run needs the installed game's folder: where its " + MasterName(engine) + " and engine live.");
			string folder = ConsoleExt.ReadLine("Game folder (blank to cancel): ", -1, false).Trim().Trim('"');
			if (folder.Length == 0) {
				problem = "Cancelled. Nothing was changed.";
			}
			if (problem.Length == 0) {
				game = Runnable(folder, engine, out string why);
				if (game == null) {
					problem = why;
				}
			}
			if (problem.Length == 0) {
				install = new GameInstall();
				install.Folder = Path.GetFullPath(folder).TrimEnd('\\');
				problem = ChooseLauncher(install, game!);
			}
			if (problem.Length == 0) {
				GameInstallList.Put(install!);
			}
			if (problem.Length > 0) {
				install = null;
			}
			return install;
		}


		/// <summary>
		/// Inspects a folder as a game of one engine. An NScripter folder with no engine of
		/// its own is still runnable through the bundled one, as long as the script is there.
		/// </summary>
		/// <returns>The location, or null with why in the out parameter.</returns>
		private static GameLocation? Runnable(string folder, CheckpointEngine engine, out string why) {
			why = "";
			GameLocation found = GameLocation.Inspect(folder);
			bool runnableThroughBundle = found.Engine == CheckpointEngine.NScripter
				&& found.Archive.Length > 0 && BundledEngine.Available == true;
			if (found.IsGame == false && runnableThroughBundle == false) {
				why = "not a runnable game folder: " + found.Reason + ".";
			}
			if (why.Length == 0 && found.Engine != engine) {
				why = "that game is " + EngineWord(found.Engine) + "; this checkpoint is " + EngineWord(engine) + ".";
			}
			GameLocation? location = null;
			if (why.Length == 0) {
				location = found;
			}
			return location;
		}


		/// <summary>
		/// Settles an install's launcher: the bundled engine first for NScripter, then every
		/// exe in the folder; asks when there is more than one, and asks once what to pass it.
		/// </summary>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string ChooseLauncher(GameInstall install, GameLocation game) {
			string problem = "";
			List<string> candidates = new();
			List<string> labels = new();
			if (game.Engine == CheckpointEngine.NScripter && BundledEngine.Available == true) {
				candidates.Add(BundledEngine.ExePath);
				labels.Add(BundledEngine.Label);
			}
			foreach (string exe in game.Launchers) {
				candidates.Add(exe);
				labels.Add(Path.GetFileName(exe));
			}

			string launcher = "";
			if (candidates.Count == 1) {
				launcher = candidates[0];
			}
			if (candidates.Count > 1) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText("What starts the game in " + install.Folder + "?");
				foreach (string label in labels) {
					menu.AddChoice(new ConsoleMenuItem(label));
				}
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				int choice = menu.GetChoice();
				if (choice >= 0 && choice < candidates.Count) {
					launcher = candidates[choice];
				}
				if (launcher.Length == 0) {
					problem = "Cancelled. Nothing was changed.";
				}
			}
			if (candidates.Count == 0) {
				problem = "Nothing can start this game: no exe in its folder and no bundled engine.";
			}
			if (problem.Length == 0) {
				// A locale bypass, for instance, takes the engine's exe name; most launchers
				// take nothing. The bundled engine takes nothing: it reads the game from its
				// working directory.
				string arguments = "";
				bool bundled = string.Equals(launcher, BundledEngine.ExePath, StringComparison.OrdinalIgnoreCase);
				if (bundled == false) {
					Console.WriteLine("Launcher: " + Path.GetFileName(launcher));
					arguments = ConsoleExt.ReadLine("Arguments to pass it, if any (blank for none): ", -1, false).Trim();
				}
				install.Launcher = launcher;
				install.Arguments = arguments;
			}
			return problem;
		}


		/// <summary>
		/// Copies the master over the live archive, backing the live one up into the
		/// checkpoint's backups\ first only when asked: the backup is taken and checked
		/// before anything is overwritten. For NScripter a live 0.txt is always moved into
		/// backups\, since the engine loads it in preference to nscript.dat and would
		/// otherwise ignore what was just installed; that is a necessity, not a backup.
		/// </summary>
		/// <param name="backUp">Whether the live archive is copied into backups\ before it is overwritten.</param>
		private static string Install(string folder, string master, GameLocation game, bool backUp) {
			string problem = "";
			string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			try {
				string backups = Path.Combine(folder, CheckpointInspector.BackupsFolder);
				Directory.CreateDirectory(backups);

				if (game.Script.Length > 0 && File.Exists(game.Script) == true) {
					string scriptBackup = Path.Combine(backups, Path.GetFileName(game.Script) + ".live_" + stamp);
					File.Move(game.Script, scriptBackup, false);
					Console.WriteLine("Moved the live " + Path.GetFileName(game.Script) + " aside to " + scriptBackup + " (the engine would load it instead of nscript.dat)");
					CheckpointLog.Warning(folder, "Run", "moved the live " + Path.GetFileName(game.Script) + " aside (" + Path.GetFileName(scriptBackup) + ")");
				}

				if (backUp == true && File.Exists(game.Archive) == true) {
					string backup = Path.Combine(backups, Path.GetFileName(game.Archive) + ".live_" + stamp);
					File.Copy(game.Archive, backup, false);
					if (File.Exists(backup) == false) {
						problem = "The backup of the live archive did not land; nothing was overwritten.";
					}
					if (problem.Length == 0) {
						Console.WriteLine("Backed up the live archive to " + backup);
					}
				}
				if (problem.Length == 0) {
					File.Copy(master, game.Archive, true);
					Console.WriteLine("Installed the master over " + game.Archive);
					CheckpointLog.Warning(folder, "Run", "installed the master over " + game.Archive);
				}
			}
			catch (Exception exception) {
				problem = "Could not install: " + exception.Message;
			}
			return problem;
		}


		/// <summary>
		/// Starts the game from its own folder, passing the launcher whatever the user said
		/// it takes.
		/// </summary>
		private static string Launch(string gameFolder, string launcher, string arguments) {
			string problem = "";
			try {
				ProcessStartInfo start = new(launcher);
				start.WorkingDirectory = gameFolder;
				start.UseShellExecute = true;
				start.Arguments = arguments;
				Process.Start(start);
				Console.WriteLine("Launched " + Path.GetFileName(launcher) + (arguments.Length > 0 ? " " + arguments : ""));
			}
			catch (Exception exception) {
				problem = "Could not start the game: " + exception.Message;
			}
			return problem;
		}


		private static string MasterName(CheckpointEngine engine) {
			string name = "Scene.pck";
			if (engine == CheckpointEngine.NScripter) {
				name = NScriptArchive.ArchiveName;
			}
			return name;
		}


		private static string EngineWord(CheckpointEngine engine) {
			string word = "unknown";
			if (engine == CheckpointEngine.NScripter) {
				word = "NScripter";
			}
			if (engine == CheckpointEngine.Siglus) {
				word = "Siglus";
			}
			return word;
		}
	}
}
