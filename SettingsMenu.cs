// File: SettingsMenu.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// High-level preferences for the whole tool. Each item shows its current value in its
	/// own text; choosing it changes that one value.
	/// </summary>
	public static class SettingsMenu {

		private static readonly ConsoleMenuItem ReportingItem = new("Send build-mode reports");
		private static readonly ConsoleMenuItem ServiceItem = new("Build-mode service address");
		private static readonly ConsoleMenuItem RootItem = new("Checkpoints folder");


		/// <summary>
		/// Shows the menu until the user chooses Back.
		/// </summary>
		public static void Show() {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			menu.AddChoice(RootItem.SetActionOnSelect(SetCheckpointsRoot));
			menu.AddChoice(ReportingItem.SetActionOnSelect(ToggleReporting));
			menu.AddChoice(ServiceItem.SetActionOnSelect(SetServiceAddress));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Puts each setting's current value into its item text.
		/// </summary>
		private static void RefreshHeader(ConsoleSelectMenu menu) {
			string reporting = "off";
			if (BuildModeService.ReportingAllowed == true) {
				reporting = "on";
			}
			if (BuildModeService.ReportingAsked == false) {
				reporting = "not decided yet";
			}
			string root = CheckpointsRoot.Folder;
			if (root.Length == 0) {
				root = "not set (asked when first needed)";
			}
			RootItem.SetText("Checkpoints folder (new checkpoints go in <folder>\\<label>): " + root);
			ReportingItem.SetText("Send build-mode reports: " + reporting);
			ServiceItem.SetText("Build-mode service address: " + BuildModeService.BaseUrl);
			menu.SetPreChoiceText("-- Settings --\n");
		}


		/// <summary>
		/// Turns reporting on or off, explaining what a report contains the first time.
		/// </summary>
		private static void ToggleReporting() {
			AskReporting();
		}


		/// <summary>
		/// Makes sure the user has decided about reporting, asking once if they never have.
		/// Called before the first report an operation wants to send.
		/// </summary>
		/// <returns>Whether reporting is allowed.</returns>
		public static bool EnsureReportingDecided() {
			if (BuildModeService.ReportingAsked == false) {
				AskReporting();
			}
			return BuildModeService.ReportingAllowed;
		}


		private static void AskReporting() {
			if (BuildModeService.ReportingAsked == false) {
				Console.WriteLine("A report tells the build-mode service which build mode worked for an archive:");
				Console.WriteLine("the archive's fingerprint, hashes of its slices, its size, the game name you gave");
				Console.WriteLine("it, the mode, and the compiler version. Nothing about you or your machine is sent.");
				Console.WriteLine();
			}
			bool allow = ConsoleExt.ReadValue<bool>("Send build-mode reports? (y/n): ", false);
			BuildModeService.ReportingAllowed = allow;
		}


		/// <summary>
		/// Sets where the tool makes checkpoint folders; blank keeps the current one. The
		/// folder is created if it does not exist.
		/// </summary>
		private static void SetCheckpointsRoot() {
			string current = CheckpointsRoot.Folder;
			if (current.Length == 0) {
				current = "(not set)";
			}
			Console.WriteLine("Current: " + current);
			string answer = ConsoleExt.ReadLine("Checkpoints folder (blank keeps it): ", -1, false).Trim().Trim('"');
			if (answer.Length > 0) {
				try {
					Directory.CreateDirectory(answer);
					CheckpointsRoot.Folder = Path.GetFullPath(answer);
					Console.WriteLine("New checkpoints will go under " + CheckpointsRoot.Folder);
				}
				catch (Exception exception) {
					Console.WriteLine("Could not use that folder: " + exception.Message + " Nothing changed.");
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}


		/// <summary>
		/// Overrides where the build-mode service is looked for; blank keeps the current
		/// address, and the word "built-in" returns to the address compiled into the tool.
		/// </summary>
		private static void SetServiceAddress() {
			Console.WriteLine("Current: " + BuildModeService.BaseUrl);
			string answer = ConsoleExt.ReadLine("New address (blank keeps it, \"built-in\" restores the default): ", -1, false).Trim();
			if (answer.Length > 0) {
				if (string.Equals(answer, "built-in", StringComparison.OrdinalIgnoreCase) == true) {
					BuildModeService.BaseUrl = "";
					Console.WriteLine("Using the built-in address: " + BuildModeService.BaseUrl);
				}
				if (string.Equals(answer, "built-in", StringComparison.OrdinalIgnoreCase) == false) {
					BuildModeService.BaseUrl = answer;
					Console.WriteLine("Using " + BuildModeService.BaseUrl);
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}
	}
}
