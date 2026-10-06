// File: GameInstallsMenu.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The game installs this machine knows, under Settings: see them, change one's
	/// launcher or arguments, or remove one. Removing an install clears the pointer on
	/// every checkpoint that used it, so their next Run asks again. New installs are added
	/// from Run, where the checkpoint that needs one is at hand.
	/// </summary>
	public static class GameInstallsMenu {

		/// <summary>
		/// Shows the menu until the user chooses Back.
		/// </summary>
		public static void Show() {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			menu.AddChoice(new ConsoleMenuItem("Change an install's launcher or arguments").SetActionOnSelect(ChangeLauncher));
			menu.AddChoice(new ConsoleMenuItem("Remove an install").SetActionOnSelect(Remove));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Lists every install above the choices: name, folder, launcher, arguments, and
		/// which checkpoints point at it.
		/// </summary>
		private static void RefreshHeader(ConsoleSelectMenu menu) {
			string header = "-- Game installs --\n";
			List<GameInstall> all = GameInstallList.All();
			if (all.Count == 0) {
				header += "None yet. Run adds one the first time a checkpoint needs a game.\n";
			}
			foreach (GameInstall install in all) {
				string arguments = "";
				if (install.Arguments.Length > 0) {
					arguments = " " + install.Arguments;
				}
				header += install.Name + "\n"
					+ "    folder:    " + install.Folder + "\n"
					+ "    launcher:  " + Path.GetFileName(install.Launcher) + arguments + "\n"
					+ "    used by:   " + UsedBy(install.Folder) + "\n";
			}
			menu.SetPreChoiceText(header);
		}


		/// <summary>
		/// The labels of the checkpoints pointing at a folder, or "no checkpoint".
		/// </summary>
		private static string UsedBy(string folder) {
			string labels = "";
			foreach (Checkpoint checkpoint in CheckpointList.All()) {
				if (GameInstall.SameFolder(checkpoint.GameFolder, folder) == true) {
					if (labels.Length > 0) {
						labels += ", ";
					}
					labels += checkpoint.Label;
				}
			}
			if (labels.Length == 0) {
				labels = "no checkpoint";
			}
			return labels;
		}


		/// <summary>
		/// Picks an install from the list, or null when there are none or the user cancels.
		/// </summary>
		private static GameInstall? Pick(string question) {
			GameInstall? picked = null;
			List<GameInstall> all = GameInstallList.All();
			if (all.Count == 0) {
				Console.WriteLine("There are no installs yet.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (all.Count > 0) {
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText(question);
				foreach (GameInstall install in all) {
					menu.AddChoice(new ConsoleMenuItem(install.Name + "   " + install.Folder));
				}
				menu.AddChoice(new ConsoleMenuItem("Cancel"));
				int choice = menu.GetChoice();
				if (choice >= 0 && choice < all.Count) {
					picked = all[choice];
				}
			}
			return picked;
		}


		/// <summary>
		/// Re-asks an install's launcher and arguments, through the same question Run uses.
		/// </summary>
		private static void ChangeLauncher() {
			GameInstall? install = Pick("Which install?");
			if (install != null) {
				GameLocation game = GameLocation.Inspect(install.Folder);
				string problem = RunOperation.ChooseLauncher(install, game);
				if (problem.Length > 0) {
					Console.WriteLine(problem);
				}
				if (problem.Length == 0) {
					GameInstallList.Put(install);
					Console.WriteLine("Changed.");
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}


		/// <summary>
		/// Removes an install after saying which checkpoints lose their pointer.
		/// </summary>
		private static void Remove() {
			GameInstall? install = Pick("Remove which install? The game itself is not touched.");
			if (install != null) {
				Console.WriteLine("Checkpoints that will ask for a game again: " + UsedBy(install.Folder));
				bool sure = ConsoleExt.ReadValue<bool>("Remove " + install.Name + " from the list? (y/n): ", false);
				if (sure == true) {
					GameInstallList.Remove(install.Folder);
					Console.WriteLine("Removed.");
				}
				if (sure == false) {
					Console.WriteLine("Kept.");
				}
				ConsoleExt.WaitForEnter("continue");
			}
		}
	}
}
