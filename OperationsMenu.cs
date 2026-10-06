// File: OperationsMenu.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The operations on the selected checkpoint, in the order of the loop:
	/// master -extract-> extract\ -split-> split\ -join-> extract\ -build-> master.
	///
	/// Extract and Split each carry their own "start over" switch, toggled with Left and
	/// Right while that item is highlighted. Off, the operation refuses to run over an
	/// existing split. On, it offers to back the split up or discard it, then runs. Both
	/// switches are off every time the menu opens, and each goes off again after its run:
	/// starting over is a deliberate act, chosen on the item it applies to.
	/// </summary>
	public static class OperationsMenu {

		private static readonly ConsoleMenuItem ExtractItem = new("Extract");
		private static readonly ConsoleMenuItem SplitItem = new("Split");

		/// <summary>Whether Extract may discard an existing split. Off whenever the menu opens.</summary>
		private static bool ExtractStartOver = false;

		/// <summary>Whether Split may discard an existing split. Off whenever the menu opens.</summary>
		private static bool SplitStartOver = false;


		/// <summary>
		/// Shows the menu until the user chooses Back.
		/// </summary>
		public static void Show() {
			ExtractStartOver = false;
			SplitStartOver = false;
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			menu.AddChoice(new ConsoleMenuItem("Recover   rebuild a missing nscript.dat or 0.txt").SetActionOnSelect(RecoverOperation.Run));
			menu.AddChoice(ExtractItem.SetActionOnSelect(RunExtract));
			menu.AddChoice(SplitItem.SetActionOnSelect(RunSplit));
			menu.AddChoice(new ConsoleMenuItem("Join      split\\ -> extract\\").SetActionOnSelect(JoinOperation.Run));
			menu.AddChoice(new ConsoleMenuItem("Build     extract\\ -> master").SetActionOnSelect(BuildOperation.Run));
			menu.AddChoice(new ConsoleMenuItem("Run       install the master into the game and start it").SetActionOnSelect(RunOperation.Run));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			ExtractItem.AddOnKeyPressAction(ToggleExtractStartOver);
			SplitItem.AddOnKeyPressAction(ToggleSplitStartOver);
			menu.GetChoice();
			ExtractItem.RemoveOnKeyPressAction(ToggleExtractStartOver);
			SplitItem.RemoveOnKeyPressAction(ToggleSplitStartOver);
		}


		/// <summary>
		/// Writes the selected checkpoint and its state above the choices, and each switch
		/// into the item that carries it.
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
			ExtractItem.SetText("Extract   master -> extract\\     <- start over: " + SwitchWord(ExtractStartOver) + " ->");
			SplitItem.SetText("Split     extract\\ -> split\\     <- start over: " + SwitchWord(SplitStartOver) + " ->");
		}


		private static string SwitchWord(bool startOver) {
			string word = "no";
			if (startOver == true) {
				word = "YES - discards the split";
			}
			return word;
		}


		private static bool IsSideways(ConsoleKeyInfo key) {
			return key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow;
		}


		/// <summary>Left or Right on Extract flips its own switch.</summary>
		private static void ToggleExtractStartOver(ConsoleMenuItem? item, ConsoleKeyInfo key) {
			if (IsSideways(key) == true) {
				ExtractStartOver = ExtractStartOver == false;
			}
		}


		/// <summary>Left or Right on Split flips its own switch.</summary>
		private static void ToggleSplitStartOver(ConsoleMenuItem? item, ConsoleKeyInfo key) {
			if (IsSideways(key) == true) {
				SplitStartOver = SplitStartOver == false;
			}
		}


		private static void RunExtract() {
			ExtractOperation.Run(ExtractStartOver);
			ExtractStartOver = false;
		}


		private static void RunSplit() {
			SplitOperation.Run(SplitStartOver);
			SplitStartOver = false;
		}
	}
}
