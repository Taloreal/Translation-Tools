// File: BuildOperation.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Build: rewrites the master from the sources in extract\. The master is backed up into
	/// backups\ first, every time, and only replaced once the new one exists. Siglus compiles
	/// through siglus-ssu under the checkpoint's recorded build mode (the standard mode, with a
	/// warning, when none was ever verified); NScripter encodes 0.txt in the tool. A record of
	/// the sources' hashes is kept for the integrity check.
	/// </summary>
	public static class BuildOperation {

		/// <summary>
		/// Runs Build on the selected checkpoint, printing as it goes, and pauses at the end.
		/// </summary>
		public static void Run() {
			Checkpoint? checkpoint = CheckpointList.Selected();
			string problem = "No checkpoint is selected.";
			string folder = "";
			if (checkpoint != null) {
				folder = CheckpointInspector.FolderOf(checkpoint.Path);
				problem = "";
				CheckpointState state = CheckpointInspector.Inspect(checkpoint.Path);
				bool siglusWithoutMode = state.Engine == CheckpointEngine.Siglus && checkpoint.HasBuildMode == false
					&& state.Form != CheckpointForm.Invalid && checkpoint.Writable == true;
				if (siglusWithoutMode == true) {
					bool found = AskForOriginalArchive(checkpoint, folder);
					bool goOn = true;
					if (found == false) {
						goOn = AskBuildMode(checkpoint, folder);
					}
					if (goOn == false) {
						problem = "Cancelled. Nothing was changed.";
					}
				}
				if (problem.Length == 0) {
					problem = Build(checkpoint, Console.WriteLine);
				}
			}
			if (problem.Length > 0) {
				Console.WriteLine(problem);
				CheckpointLog.Error(folder, "Build", problem);
			}
			if (problem.Length == 0) {
				Console.WriteLine("Built the master from extract\\.");
			}
			CheckpointWatch.MarkStale();
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// Does the build. Printing goes through onLine, so this can be exercised without a
		/// console.
		/// </summary>
		/// <param name="checkpoint">The checkpoint to build.</param>
		/// <param name="onLine">Receives progress and the compiler's output.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Build(Checkpoint checkpoint, Action<string> onLine) {
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
			if (problem.Length == 0 && state.Form == CheckpointForm.Packed) {
				problem = "Nothing to build from: extract\\ holds no sources. Extract first.";
			}

			if (problem.Length == 0 && state.Engine == CheckpointEngine.NScripter) {
				problem = BuildNscripter(folder, onLine);
			}
			if (problem.Length == 0 && state.Engine == CheckpointEngine.Siglus) {
				problem = BuildSiglus(checkpoint, folder, onLine);
			}

			if (problem.Length == 0) {
				int recorded = SourceHashes.Write(folder);
				onLine("Recorded " + recorded + " source file hashes for the integrity check.");
				CheckpointLog.Warning(folder, "Build", "built the master from extract\\");
			}
			return problem;
		}


		/// <summary>
		/// No build mode is recorded: first asks for an untouched Scene.pck of the same game,
		/// since an original reproduces under its mode where a rebuilt or translated master may
		/// not. The mode found for it is recorded on the checkpoint and reported if verified here.
		/// </summary>
		/// <returns>True when a mode was found and recorded; false to fall through to the next question.</returns>
		private static bool AskForOriginalArchive(Checkpoint checkpoint, string folder) {
			bool found = false;
			Console.WriteLine("No build mode is recorded for \"" + checkpoint.Label + "\".");
			Console.WriteLine("The surest way to find it is from an untouched Scene.pck of the same game - the installed game's own, for instance.");
			string archive = ConsoleExt.ReadLine("Path to one, if you have it (blank to skip): ", -1, false).Trim().Trim('"');
			if (archive.Length > 0) {
				if (File.Exists(archive) == false) {
					Console.WriteLine("Nothing at " + archive + ". Moving on.");
				}
				if (File.Exists(archive) == true) {
					BuildModeFinder.Finding finding = BuildModeFinder.Find(archive, Console.WriteLine);
					if (finding.BuildMode == Checkpoint.NoBuildMode) {
						Console.WriteLine("No build mode reproduced that archive. Moving on.");
					}
					if (finding.BuildMode != Checkpoint.NoBuildMode) {
						checkpoint.BuildMode = finding.BuildMode;
						checkpoint.CompilerVersion = SiglusCompiler.Version();
						CheckpointList.Update(checkpoint.Label, checkpoint);
						CheckpointInfo info = CheckpointInfo.Load(folder);
						if (info.GameName.Length == 0 && finding.MatchedGame != null) {
							info.GameName = finding.MatchedGame.GameName;
							info.VndbId = finding.MatchedGame.VndbId;
							string saveProblem = info.Save(folder);
							if (saveProblem.Length > 0) {
								CheckpointLog.Warning(folder, "Build", saveProblem);
							}
						}
						Console.WriteLine("Recorded the build mode, found from " + finding.Source + ".");
						CheckpointLog.Warning(folder, "Build", "build mode found from " + finding.Source + " using " + archive);
						BuildModeFinder.ReportIfVerified(finding, info.GameName, info.VndbId, Console.WriteLine);
						found = true;
					}
				}
			}
			return found;
		}


		/// <summary>
		/// No build mode is recorded: asks whether the user knows which game this is, offering
		/// the games the installed compiler names. A pick is recorded on the checkpoint - as the
		/// user's word, not a verified mode, which the log says. "I don't know" leaves the
		/// standard-mode fallback to run.
		/// </summary>
		/// <returns>False when the user cancelled.</returns>
		private static bool AskBuildMode(Checkpoint checkpoint, string folder) {
			bool goOn = true;
			List<SiglusCompiler.KnownMode> modes = SiglusCompiler.KnownModes();
			if (modes.Count > 0) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText("No build mode is recorded for \"" + checkpoint.Label + "\". Most Siglus games build the standard way;\n"
					+ "a few need special handling. If you know this game is one of them, pick it.");
				foreach (SiglusCompiler.KnownMode mode in modes) {
					menu.AddChoice(new ConsoleMenuItem(mode.Game));
				}
				menu.AddChoice(new ConsoleMenuItem("Standard (most games)"));
				menu.AddChoice(new ConsoleMenuItem("I don't know"));
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				int choice = menu.GetChoice();
				int picked = Checkpoint.NoBuildMode;
				string pickedGame = "";
				if (choice >= 0 && choice < modes.Count) {
					picked = modes[choice].Number;
					pickedGame = modes[choice].Game;
				}
				if (choice == modes.Count) {
					picked = SiglusCompiler.StandardMode;
					pickedGame = "standard";
				}
				if (choice == modes.Count + 2) {
					goOn = false;
				}
				if (picked != Checkpoint.NoBuildMode) {
					Checkpoint updated = checkpoint;
					updated.BuildMode = picked;
					updated.CompilerVersion = SiglusCompiler.Version();
					CheckpointList.Update(checkpoint.Label, updated);
					checkpoint.BuildMode = picked;
					checkpoint.CompilerVersion = updated.CompilerVersion;
					Console.WriteLine("Recorded: builds for " + pickedGame + ".");
					CheckpointLog.Warning(folder, "Build", "build mode set by the user (" + pickedGame + "), not verified by a round-trip");
				}
			}
			return goOn;
		}


		/// <summary>
		/// NScripter: back the master up, then encode extract\0.txt over it.
		/// </summary>
		private static string BuildNscripter(string folder, Action<string> onLine) {
			string master = Path.Combine(folder, NScriptArchive.ArchiveName);
			string script = Path.Combine(folder, CheckpointInspector.ExtractFolder, NScriptArchive.ScriptName);
			string problem = BackUpMaster(folder, master, onLine);
			if (problem.Length == 0) {
				onLine("Encoding " + NScriptArchive.ScriptName + " into " + NScriptArchive.ArchiveName + "...");
				problem = NScriptArchive.EncodeFile(script, master);
			}
			return problem;
		}


		/// <summary>
		/// Siglus: compile extract\ to a new archive beside the backups, and only then move it
		/// over the master. A compile that fails leaves the master untouched.
		/// </summary>
		public static string BuildSiglus(Checkpoint checkpoint, string folder, Action<string> onLine) {
			string problem = "";
			if (SiglusCompiler.Available == false) {
				problem = SiglusCompiler.NotInstalledMessage;
			}

			int buildMode = checkpoint.BuildMode;
			if (problem.Length == 0 && buildMode == Checkpoint.NoBuildMode) {
				buildMode = SiglusCompiler.StandardMode;
				onLine("No verified build mode for this checkpoint; building under the standard mode.");
				CheckpointLog.Warning(folder, "Build", "built under the standard mode: no verified build mode");
			}

			string master = Path.Combine(folder, "Scene.pck");
			string extract = Path.Combine(folder, CheckpointInspector.ExtractFolder);
			string built = Path.Combine(folder, CheckpointInspector.BackupsFolder, "Scene.pck.building_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
			if (problem.Length == 0) {
				Directory.CreateDirectory(Path.Combine(folder, CheckpointInspector.BackupsFolder));
				onLine("Compiling extract\\ with " + SiglusCompiler.Version() + "...");
				problem = SiglusCompiler.Compile(extract, built, buildMode, onLine);
			}
			if (problem.Length == 0) {
				problem = BackUpMaster(folder, master, onLine);
			}
			if (problem.Length == 0) {
				try {
					File.Move(built, master, true);
					onLine("Replaced the master with the new archive.");
					// What this master was built with is a fact about the file, kept with it.
					CheckpointInfo info = CheckpointInfo.Load(folder);
					info.BuiltWith = buildMode;
					string saveProblem = info.Save(folder);
					if (saveProblem.Length > 0) {
						CheckpointLog.Warning(folder, "Build", saveProblem);
					}
				}
				catch (Exception exception) {
					problem = "Compiled, but could not replace the master: " + exception.Message + " The new archive is at " + built;
				}
			}
			if (problem.Length > 0 && File.Exists(built) == true && File.Exists(master) == true) {
				// A build that will not be used is not kept beside the real backups.
				try { File.Delete(built); } catch (Exception) { }
			}
			return problem;
		}


		/// <summary>
		/// Copies the master into backups\ with a stamp before it is rewritten. A master that
		/// is not there (an NScripter checkpoint recovered from 0.txt, say) needs no backup.
		/// </summary>
		private static string BackUpMaster(string folder, string master, Action<string> onLine) {
			string problem = "";
			if (File.Exists(master) == true) {
				try {
					string backups = Path.Combine(folder, CheckpointInspector.BackupsFolder);
					Directory.CreateDirectory(backups);
					string target = Path.Combine(backups, Path.GetFileName(master) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
					File.Copy(master, target, false);
					onLine("Backed up the master to " + target);
				}
				catch (Exception exception) {
					problem = "Could not back up the master: " + exception.Message + " Nothing was changed.";
				}
			}
			return problem;
		}
	}
}
