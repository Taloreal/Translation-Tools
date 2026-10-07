// File: ExtractOperation.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Extract: fills a checkpoint's extract\ from its master. NScripter decodes nscript.dat
	/// to 0.txt in the tool. Siglus settles which game and which build mode first - by the
	/// hash set against known archives, then the service, then the compiler's own
	/// round-trip - records them on the checkpoint, reports if allowed, and runs the
	/// compiler's extract under that mode. A populated extract\ is never overwritten
	/// without asking: back up to backups\, overwrite, or cancel.
	/// </summary>
	public static class ExtractOperation {

		/// <summary>
		/// Runs Extract on the selected checkpoint, printing as it goes. Always pauses at the
		/// end so the result stays on screen.
		/// </summary>
		/// <param name="startOver">True to discard an existing split (after offering a backup) instead of refusing.</param>
		public static void Run(bool startOver) {
			Checkpoint? checkpoint = CheckpointList.Selected();
			string problem = "";
			if (checkpoint == null) {
				problem = "No checkpoint is selected.";
			}
			if (problem.Length == 0 && checkpoint!.Writable == false) {
				problem = "\"" + checkpoint.Label + "\" is locked. Unlock it to extract.";
			}

			CheckpointState state = new();
			string folder = "";
			if (problem.Length == 0) {
				folder = CheckpointInspector.FolderOf(checkpoint!.Path);
				state = CheckpointInspector.Inspect(checkpoint.Path);
				if (state.Form == CheckpointForm.Invalid) {
					problem = "\"" + checkpoint.Label + "\" is invalid: " + state.Reason;
				}
				if (problem.Length == 0 && state.Form == CheckpointForm.Split && startOver == false) {
					problem = "\"" + checkpoint.Label + "\" is already split. Extracting again would orphan the split. Turn on start over (Left or Right on Extract) to discard it.";
				}
			}

			bool proceed = problem.Length == 0;
			if (proceed == true && state.Form == CheckpointForm.Split) {
				proceed = FolderClearing.ClearWithChoice(folder, new string[] { CheckpointInspector.SplitFolder, CheckpointInspector.ExtractFolder },
					"Start over discards split\\ - your edits - and extract\\, then extracts the master afresh.", "startover");
			}
			if (proceed == true && state.Form != CheckpointForm.Split) {
				proceed = FolderClearing.ClearWithChoice(folder, new string[] { CheckpointInspector.ExtractFolder },
					"extract\\ already holds the sources Join writes to. Extracting again replaces them.", "extract");
			}
			if (proceed == true && state.Engine == CheckpointEngine.NScripter) {
				problem = ExtractNscripter(folder);
				if (problem.Length == 0) {
					EnsureGameRecorded(checkpoint!, folder);
				}
			}
			if (proceed == true && state.Engine == CheckpointEngine.Siglus) {
				problem = ExtractSiglus(checkpoint!, folder);
			}

			if (problem.Length > 0) {
				Console.WriteLine(problem);
				CheckpointLog.Error(folder, "Extract", problem);
			}
			if (problem.Length == 0 && proceed == false) {
				Console.WriteLine("Cancelled. Nothing was changed.");
			}
			if (problem.Length == 0 && proceed == true) {
				Console.WriteLine("Extracted into " + Path.Combine(folder, CheckpointInspector.ExtractFolder));
			}
			CheckpointWatch.MarkStale();
			ConsoleExt.WaitForEnter("continue");
		}


		/// <summary>
		/// NScripter: the game is asked once per checkpoint too, so the glossary and anything
		/// else that keys on the game has one. No known-game suggestion: nothing fingerprints
		/// an NScripter master. Then a same-game glossary is offered, if one exists.
		/// </summary>
		private static void EnsureGameRecorded(Checkpoint checkpoint, string folder) {
			SetGame(checkpoint, folder, false);
		}


		/// <summary>
		/// Records which game a checkpoint is: a Siglus master is fingerprinted first and a
		/// known archive becomes the suggestion; then the game question, the same one Extract
		/// asks. A new answer is written to checkpoint.info and a same-game glossary offered.
		/// </summary>
		/// <param name="checkpoint">The checkpoint.</param>
		/// <param name="folder">Its folder.</param>
		/// <param name="alwaysAsk">True to ask even when a game is recorded (the menu); false to ask only when none is.</param>
		public static void SetGame(Checkpoint checkpoint, string folder, bool alwaysAsk) {
			CheckpointInfo info = CheckpointInfo.Load(folder);
			if (info.GameName.Length == 0 || alwaysAsk == true) {
				if (info.GameName.Length > 0) {
					Console.WriteLine("Game now: " + info.GameName);
				}
				KnownGame? suggestion = null;
				string master = Path.Combine(folder, "Scene.pck");
				if (File.Exists(master) == true) {
					try {
						Console.WriteLine("Reading the archive...");
						KnownArchive? matched = BuildModeService.FindKnownArchiveByHashSet(BuildModeService.SegmentHashes(master), out suggestion, out int shared);
						if (matched == null) {
							matched = BuildModeService.FindKnownArchive(BuildModeService.Fingerprint(master), out suggestion);
						}
					}
					catch (Exception) {
						// An unreadable master just means no suggestion.
					}
				}
				string name = AskGame(suggestion, out string vndbId);
				if (name.Length > 0) {
					info.GameName = name;
					info.VndbId = vndbId;
					if (info.VndbId.Length == 0) {
						CheckpointLog.Warning(folder, "Game", "title not canonized: \"" + info.GameName + "\" has no VNDB id");
					}
					string saveProblem = info.Save(folder);
					if (saveProblem.Length > 0) {
						Console.WriteLine(saveProblem);
						CheckpointLog.Warning(folder, "Game", saveProblem);
					}
					if (saveProblem.Length == 0) {
						Console.WriteLine("Game: " + info.GameName + ".");
						GlossariesMenu.OfferFromSameGame(checkpoint, folder, false);
					}
				}
			}
		}


		/// <summary>
		/// NScripter: decode the master to extract\0.txt.
		/// </summary>
		private static string ExtractNscripter(string folder) {
			string master = Path.Combine(folder, NScriptArchive.ArchiveName);
			string script = Path.Combine(folder, CheckpointInspector.ExtractFolder, NScriptArchive.ScriptName);
			Console.WriteLine("Decoding " + NScriptArchive.ArchiveName + "...");
			return NScriptArchive.DecodeFile(master, script);
		}


		/// <summary>
		/// Siglus: settle the game and build mode, record them, report if allowed, then let
		/// the compiler extract under that mode.
		/// </summary>
		private static string ExtractSiglus(Checkpoint checkpoint, string folder) {
			string problem = "";
			if (SiglusCompiler.Available == false) {
				Console.WriteLine("siglus-ssu, the compiler that reads Siglus archives, is not installed.");
				bool install = ConsoleExt.ReadValue<bool>("Install it now? It finds or installs Python and then the compiler. (y/n): ", false);
				if (install == true) {
					bool installed = SiglusCompiler.Install(Console.WriteLine);
					if (installed == false) {
						problem = "The compiler is still not available. Nothing was changed.";
					}
				}
				if (install == false) {
					problem = SiglusCompiler.NotInstalledMessage;
				}
			}

			string master = Path.Combine(folder, "Scene.pck");

			// Which game, then which build mode. The finder does the fingerprint, the hash-set
			// match, the service and the round-trip, in that order; the game question sits
			// between the match and the search so a matched game is the suggestion.
			// The game is asked once per checkpoint: checkpoint.info remembers the answer.
			BuildModeFinder.Finding finding = new();
			CheckpointInfo info = CheckpointInfo.Load(folder);
			if (problem.Length == 0 && info.GameName.Length > 0) {
				Console.WriteLine("Game: " + info.GameName);
			}
			if (problem.Length == 0 && info.GameName.Length == 0) {
				Console.WriteLine("Reading the archive...");
				finding.HashSet = BuildModeService.SegmentHashes(master);
				finding.Fingerprint = BuildModeService.Fingerprint(master);
				KnownArchive? matched = BuildModeService.FindKnownArchiveByHashSet(finding.HashSet, out KnownGame? matchedGame, out int shared);
				if (matched == null) {
					matched = BuildModeService.FindKnownArchive(finding.Fingerprint, out matchedGame);
				}
				info.GameName = AskGame(matchedGame, out info.VndbId);
				if (info.GameName.Length == 0) {
					problem = "Cancelled. Nothing was changed.";
				}
				if (info.GameName.Length > 0 && info.VndbId.Length == 0) {
					CheckpointLog.Warning(folder, "Extract", "title not canonized: \"" + info.GameName + "\" has no VNDB id");
				}
				if (info.GameName.Length > 0) {
					string saveProblem = info.Save(folder);
					if (saveProblem.Length > 0) {
						Console.WriteLine(saveProblem + " Extract will ask again next time.");
						CheckpointLog.Warning(folder, "Extract", saveProblem);
					}
					if (saveProblem.Length == 0) {
						GlossariesMenu.OfferFromSameGame(checkpoint, folder, false);
					}
				}
			}

			int buildMode = Checkpoint.NoBuildMode;
			string compilerVersion = "";
			string warning = "";
			if (problem.Length == 0) {
				compilerVersion = SiglusCompiler.Version();
				finding = BuildModeFinder.Find(master, Console.WriteLine);
				buildMode = finding.BuildMode;
				if (buildMode == Checkpoint.NoBuildMode) {
					// No mode reproduced the archive. The sources are still extracted, under
					// the standard mode, but the master is backed up first - not optionally -
					// and the checkpoint carries a warning until a mode is proven.
					problem = BackUpMaster(folder, master);
					if (problem.Length == 0) {
						warning = "build mode unverified; extracted under the standard mode";
						Console.WriteLine("No build mode reproduced this archive. Extracting under the standard mode; the checkpoint is marked.");
						CheckpointLog.Warning(folder, "Extract", warning + " (compiler " + compilerVersion + ")");
					}
				}
			}

			if (problem.Length == 0) {
				checkpoint.BuildMode = buildMode;
				checkpoint.CompilerVersion = compilerVersion;
				checkpoint.Warning = warning;
				CheckpointList.Update(checkpoint.Label, checkpoint);
				BuildModeFinder.ReportIfVerified(finding, info.GameName, info.VndbId, Console.WriteLine);
			}

			if (problem.Length == 0) {
				Console.WriteLine("Extracting the sources...");
				string destination = Path.Combine(folder, CheckpointInspector.ExtractFolder);
				int extractMode = buildMode == Checkpoint.NoBuildMode
					? SiglusCompiler.StandardMode
					: buildMode;
				problem = SiglusCompiler.Extract(master, destination, extractMode, Console.WriteLine);
			}
			return problem;
		}


		/// <summary>
		/// Copies the master into backups\ with a stamp, before an extraction the tool cannot
		/// vouch for. The master itself is only read.
		/// </summary>
		/// <returns>Empty on success, otherwise a plain sentence; nothing else happens on failure.</returns>
		private static string BackUpMaster(string folder, string master) {
			string problem = "";
			try {
				string backups = Path.Combine(folder, CheckpointInspector.BackupsFolder);
				Directory.CreateDirectory(backups);
				string target = Path.Combine(backups, Path.GetFileName(master) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
				File.Copy(master, target, false);
				Console.WriteLine("Backed up the master to " + target);
			}
			catch (Exception exception) {
				problem = "Could not back up the master: " + exception.Message + " Nothing was changed.";
			}
			return problem;
		}


		/// <summary>
		/// Asks which game the archive is. A suggestion, when there is one, is the first
		/// choice; every other known game follows, then "another name", which goes through
		/// VNDB so the name is canonized to VNDB's title and id.
		/// </summary>
		/// <param name="suggestion">The game the hash set matched, or null.</param>
		/// <param name="vndbId">VNDB's id for the chosen game; empty when it has none.</param>
		/// <returns>The game name, or empty when cancelled.</returns>
		private static string AskGame(KnownGame? suggestion, out string vndbId) {
			ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
			List<KnownGame> offered = new();
			if (suggestion != null) {
				menu.SetPreChoiceText("This archive looks like " + suggestion.GameName + ".");
				menu.AddChoice(new ConsoleMenuItem("Yes, it is " + suggestion.GameName));
				offered.Add(suggestion);
			}
			if (suggestion == null) {
				menu.SetPreChoiceText("Which game is this archive?");
			}
			foreach (KnownGame known in BuildModeService.KnownGames) {
				bool isSuggestion = suggestion != null && CheckpointList.SameLabel(known.GameName, suggestion.GameName) == true;
				if (isSuggestion == false) {
					menu.AddChoice(new ConsoleMenuItem(known.GameName));
					offered.Add(known);
				}
			}
			int anotherIndex = offered.Count;
			menu.AddChoice(new ConsoleMenuItem("Another name..."));
			menu.AddChoice(new ConsoleMenuItem("Cancel"));

			int choice = menu.GetChoice();
			string gameName = "";
			vndbId = "";
			if (choice >= 0 && choice < offered.Count) {
				gameName = offered[choice].GameName;
				vndbId = offered[choice].VndbId;
			}
			if (choice == anotherIndex) {
				string typed = ConsoleExt.ReadLine("Game name: ", -1, false).Trim();
				if (typed.Length > 0) {
					gameName = Canonize(typed, out vndbId);
				}
			}
			return gameName;
		}


		/// <summary>
		/// Looks a typed title up on VNDB and lets the user pick the game it means. The pick
		/// gives VNDB's title and id; "none of these", or VNDB being unreachable, keeps the
		/// typed name with no id.
		/// </summary>
		/// <param name="typed">What the user typed.</param>
		/// <param name="vndbId">VNDB's id for the pick; empty when there was none.</param>
		/// <returns>The canonical title, or the typed name.</returns>
		private static string Canonize(string typed, out string vndbId) {
			string gameName = typed;
			vndbId = "";
			Console.WriteLine("Asking VNDB about \"" + typed + "\"...");
			List<VndbCandidate> candidates = VndbClient.Search(typed, 5, out string error);
			if (error.Length > 0) {
				Console.WriteLine(error + " Keeping the name as typed.");
			}
			if (error.Length == 0 && candidates.Count == 0) {
				Console.WriteLine("VNDB has nothing like that. Keeping the name as typed.");
			}
			if (candidates.Count > 0) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText("VNDB found these for \"" + typed + "\". Which did you mean?");
				foreach (VndbCandidate candidate in candidates) {
					menu.AddChoice(new ConsoleMenuItem(candidate.Describe()));
				}
				menu.AddChoice(new ConsoleMenuItem("None of these - keep \"" + typed + "\""));
				int pick = menu.GetChoice();
				if (pick >= 0 && pick < candidates.Count) {
					gameName = candidates[pick].Title;
					vndbId = candidates[pick].Id;
				}
			}
			return gameName;
		}
	}
}
