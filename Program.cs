// File: Program.cs
// Namespace: TranslationTools
using System.Text;

using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The console shell. The top menu is the place a checkpoint is selected: Left and Right
	/// on the selector cycle through the list, Enter on it opens the checkpoint manager.
	/// Settings holds the tool's high-level preferences. Operations on the selected
	/// checkpoint register here as they are ported in.
	/// </summary>
	public static class Program {

		private static readonly ConsoleMenuItem SelectorItem = new("<- no checkpoints ->");


		[STAThread]
		public static void Main(string[] arguments) {
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			LoadKnownGames();
			RunMainMenu();
		}


		/// <summary>
		/// Asks the build-mode service for the games it knows. A service that cannot be
		/// reached is reported in the header, never an error: the tool works without it.
		/// </summary>
		private static void LoadKnownGames() {
			Console.WriteLine("Asking the build-mode service for its game list...");
			string problem = BuildModeService.RefreshKnownGames();
			if (problem.Length > 0) {
				Console.WriteLine(problem);
			}
		}


		/// <summary>
		/// Shows the top menu until the user chooses Exit.
		/// </summary>
		private static void RunMainMenu() {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			SelectorItem.AddOnKeyPressAction(CycleCheckpoint);
			menu.AddChoice(SelectorItem.SetActionOnSelect(CheckpointsMenu.Show));
			menu.AddChoice(new ConsoleMenuItem("Extract / Split / Join / Build").SetActionOnSelect(OperationsMenu.Show));
			menu.AddChoice(new ConsoleMenuItem("Settings").SetActionOnSelect(SettingsMenu.Show));
			menu.AddChoice(new ConsoleMenuItem("Exit"));
			menu.GetChoice();
			CheckpointWatch.Release();
		}


		/// <summary>
		/// Writes the service state above the choices and puts the selected checkpoint into
		/// the selector's text.
		/// </summary>
		private static void RefreshHeader(ConsoleSelectMenu menu) {
			string games = "Known games: " + BuildModeService.KnownGames.Count;
			if (BuildModeService.KnownGamesSource == "cache") {
				games += " (from last time; the service could not be reached)";
			}
			if (BuildModeService.KnownGamesSource == "none") {
				games = "Known games: none (the service could not be reached)";
			}
			menu.SetPreChoiceText(games + "\n");

			Checkpoint? selected = CheckpointList.Selected();
			CheckpointWatch.Focus(selected);
			if (selected == null) {
				SelectorItem.SetText("<- no checkpoints - press Enter to add one ->");
			}
			if (selected != null) {
				int count = CheckpointList.All().Count;
				string game = "";
				CheckpointInfo info = CheckpointInfo.Load(CheckpointInspector.FolderOf(selected.Path), CheckpointWatch.State);
				if (info.GameName.Length > 0) {
					game = "  ·  " + info.GameName;
				}
				if (selected.Warning.Length > 0) {
					game += "  ·  WARNING: " + selected.Warning;
				}
				SelectorItem.SetText("<- " + selected.Label
					+ "  ·  " + CheckpointWatch.State.Describe()
					+ game
					+ "  ·  " + selected.StateWord
					+ "   " + (IndexOfSelected() + 1) + "/" + count + " ->");
			}
		}


		/// <summary>
		/// Left and Right on the selector move the selection through the list.
		/// </summary>
		private static void CycleCheckpoint(ConsoleMenuItem? item, ConsoleKeyInfo key) {
			if (key.Key == ConsoleKey.LeftArrow) {
				CheckpointList.CycleSelection(-1);
			}
			if (key.Key == ConsoleKey.RightArrow) {
				CheckpointList.CycleSelection(1);
			}
		}


		private static int IndexOfSelected() {
			int found = 0;
			List<Checkpoint> all = CheckpointList.All();
			string label = CheckpointList.SelectedLabel;
			for (int index = 0; index < all.Count; index++) {
				if (CheckpointList.SameLabel(all[index].Label, label) == true) {
					found = index;
				}
			}
			return found;
		}
	}
}
