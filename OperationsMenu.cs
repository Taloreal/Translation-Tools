// File: OperationsMenu.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The operations on the selected checkpoint, in the order of the loop:
	/// master -extract-> extract\ -split-> split\ -join-> extract\ -build-> master.
	/// </summary>
	public static class OperationsMenu {

		/// <summary>
		/// Shows the menu until the user chooses Back.
		/// </summary>
		public static void Show() {
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			menu.AddChoice(new ConsoleMenuItem("Recover   rebuild a missing nscript.dat or 0.txt").SetActionOnSelect(RecoverOperation.Run));
			menu.AddChoice(new ConsoleMenuItem("Extract   master -> extract\\").SetActionOnSelect(ExtractOperation.Run));
			menu.AddChoice(new ConsoleMenuItem("Split     extract\\ -> split\\").SetActionOnSelect(NotBuiltYet));
			menu.AddChoice(new ConsoleMenuItem("Join      split\\ -> extract\\").SetActionOnSelect(NotBuiltYet));
			menu.AddChoice(new ConsoleMenuItem("Build     extract\\ -> master").SetActionOnSelect(BuildOperation.Run));
			menu.AddChoice(new ConsoleMenuItem("Run       install the master into the game and start it").SetActionOnSelect(RunOperation.Run));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			menu.GetChoice();
		}


		/// <summary>
		/// Writes the selected checkpoint and its state above the choices.
		/// </summary>
		private static void RefreshHeader(ConsoleSelectMenu menu) {
			Checkpoint? selected = CheckpointList.Selected();
			CheckpointWatch.Focus(selected);
			string header = "-- Operations --\n";
			if (selected == null) {
				header += "No checkpoint is selected.\n";
			}
			if (selected != null) {
				string game = "";
				CheckpointInfo info = CheckpointInfo.Load(CheckpointInspector.FolderOf(selected.Path), CheckpointWatch.State);
				if (info.GameName.Length > 0) {
					game = "  ·  " + info.GameName;
				}
				if (selected.Warning.Length > 0) {
					game += "  ·  WARNING: " + selected.Warning;
				}
				header += selected.Label + "  ·  " + CheckpointWatch.State.Describe() + game + "  ·  " + selected.StateWord + "\n"
					+ "Path: " + selected.Path + "\n";
			}
			menu.SetPreChoiceText(header);
		}


		private static void NotBuiltYet() {
			Console.WriteLine("Not built yet.");
			ConsoleExt.WaitForEnter("continue");
		}
	}
}
