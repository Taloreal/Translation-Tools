// File: RunOperation.cs
// Namespace: TranslationTools
using System.Diagnostics;
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Run: installs the checkpoint's master into the game and starts the game. The live
	/// archive is backed up into the checkpoint's backups\ first, every time. The game
	/// folder and launcher are asked for once and recorded on the checkpoint; Run is gated
	/// on that folder holding the engine's start-up files beside the archive.
	/// </summary>
	public static class RunOperation {

		/// <summary>
		/// Runs on the selected checkpoint, printing as it goes, and pauses at the end.
		/// </summary>
		public static void Run() {
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
			if (problem.Length == 0) {
				game = EnsureGameFolder(checkpoint!, state.Engine, out problem);
			}
			string launcher = "";
			if (problem.Length == 0) {
				launcher = EnsureLauncher(checkpoint!, game!, out problem);
			}

			if (problem.Length == 0) {
				problem = Install(folder, master, game!);
			}
			if (problem.Length == 0) {
				problem = Launch(checkpoint!.GameFolder, launcher, checkpoint.LauncherArguments);
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
		/// The recorded game folder, or asks for one and records it. The folder must hold the
		/// engine's start-up files beside the live archive, and the archive must be the
		/// checkpoint's engine.
		/// </summary>
		private static GameLocation? EnsureGameFolder(Checkpoint checkpoint, CheckpointEngine engine, out string problem) {
			problem = "";
			GameLocation? game = null;
			string folder = checkpoint.GameFolder;
			if (folder.Length == 0) {
				Console.WriteLine("Run needs the installed game's folder: where its " + MasterName(engine) + " and engine live.");
				folder = ConsoleExt.ReadLine("Game folder (blank to cancel): ", -1, false).Trim().Trim('"');
				if (folder.Length == 0) {
					problem = "Cancelled. Nothing was changed.";
				}
			}
			if (problem.Length == 0) {
				GameLocation found = GameLocation.Inspect(folder);
				// An NScripter folder with no engine of its own is still runnable through the
				// bundled one, as long as the script is there.
				bool runnableThroughBundle = found.Engine == CheckpointEngine.NScripter
					&& found.Archive.Length > 0 && BundledEngine.Available == true;
				if (found.IsGame == false && runnableThroughBundle == false) {
					problem = "That is not a runnable game folder: " + found.Reason + ".";
				}
				if (problem.Length == 0 && found.Engine != engine) {
					problem = "That game is " + EngineWord(found.Engine) + "; this checkpoint is " + EngineWord(engine) + ".";
				}
				if (problem.Length == 0) {
					game = found;
					if (string.Equals(checkpoint.GameFolder, folder, StringComparison.OrdinalIgnoreCase) == false) {
						Checkpoint updated = checkpoint;
						updated.GameFolder = Path.GetFullPath(folder).TrimEnd('\\');
						CheckpointList.Update(checkpoint.Label, updated);
					}
				}
			}
			return game;
		}


		/// <summary>
		/// The recorded launcher, or asks which exe starts the game when there is more than
		/// one, and records it.
		/// </summary>
		private static string EnsureLauncher(Checkpoint checkpoint, GameLocation game, out string problem) {
			problem = "";
			string launcher = checkpoint.Launcher;
			if (launcher.Length > 0 && File.Exists(launcher) == false) {
				launcher = "";
			}

			// The choices: the bundled engine first for NScripter, then every exe in the folder.
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

			if (launcher.Length == 0 && candidates.Count == 1) {
				launcher = candidates[0];
			}
			if (launcher.Length == 0 && candidates.Count > 1) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText("What starts the game?");
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
			if (launcher.Length == 0 && candidates.Count == 0) {
				problem = "Nothing can start this game: no exe in its folder and no bundled engine.";
			}
			if (problem.Length == 0 && string.Equals(checkpoint.Launcher, launcher, StringComparison.OrdinalIgnoreCase) == false) {
				// A new launcher: ask once what to pass it. A locale bypass, for instance,
				// takes the engine's exe name; most launchers take nothing. The bundled
				// engine takes nothing: it reads the game from its working directory.
				string arguments = "";
				bool bundled = string.Equals(launcher, BundledEngine.ExePath, StringComparison.OrdinalIgnoreCase);
				if (bundled == false) {
					Console.WriteLine("Launcher: " + Path.GetFileName(launcher));
					arguments = ConsoleExt.ReadLine("Arguments to pass it, if any (blank for none): ", -1, false).Trim();
				}
				Checkpoint updated = checkpoint;
				updated.Launcher = launcher;
				updated.LauncherArguments = arguments;
				CheckpointList.Update(checkpoint.Label, updated);
				checkpoint.Launcher = launcher;
				checkpoint.LauncherArguments = arguments;
			}
			return launcher;
		}


		/// <summary>
		/// Backs the live archive up into the checkpoint's backups\, then copies the master
		/// over it. The backup is taken and checked before anything is overwritten. For
		/// NScripter a live 0.txt is moved into backups\ as well, since the engine loads it
		/// in preference to nscript.dat and would otherwise ignore what was just installed.
		/// </summary>
		private static string Install(string folder, string master, GameLocation game) {
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

				if (File.Exists(game.Archive) == true) {
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
