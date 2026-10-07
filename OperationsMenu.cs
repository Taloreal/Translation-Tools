// File: OperationsMenu.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The operations on the selected checkpoint, in the order of the loop:
	/// master -extract-> extract\ -split-> split\ -join-> extract\ -build-> master.
	///
	/// Four items carry a switch, toggled with Left and Right while that item is
	/// highlighted, each its own, all off every time the menu opens and off again after
	/// the item runs. Extract and Split carry "start over": off, they refuse to run over an
	/// existing split; on, they offer to back it up or discard it, then run. Join and Build
	/// carry "back up first": they always overwrite and never ask, since iterations are
	/// many; on, a copy is taken into backups\ before they write. Starting over is a
	/// deliberate act, and so is a backup on the fast path.
	/// </summary>
	public static class OperationsMenu {

		private static readonly ConsoleMenuItem ExtractItem = new("Extract");
		private static readonly ConsoleMenuItem SplitItem = new("Split");
		private static readonly ConsoleMenuItem JoinItem = new("Join");
		private static readonly ConsoleMenuItem BuildItem = new("Build");

		/// <summary>Whether Extract may discard an existing split. Off whenever the menu opens.</summary>
		private static bool ExtractStartOver = false;

		/// <summary>Whether Split may discard an existing split. Off whenever the menu opens.</summary>
		private static bool SplitStartOver = false;

		/// <summary>Whether Join copies extract\ into backups\ before writing over it. Off whenever the menu opens.</summary>
		private static bool JoinBackUp = false;

		/// <summary>Whether Build copies the master into backups\ before replacing it. Off whenever the menu opens.</summary>
		private static bool BuildBackUp = false;


		/// <summary>
		/// Shows the menu until the user chooses Back.
		/// </summary>
		public static void Show() {
			ExtractStartOver = false;
			SplitStartOver = false;
			JoinBackUp = false;
			BuildBackUp = false;
			ConsoleSelectMenu menu = new(loops: true, numbered: false, clearOnRefresh: true);
			menu.AddOnDrawMenuAction(RefreshHeader);
			menu.AddChoice(new ConsoleMenuItem("Recover   rebuild a missing master or its sources").SetActionOnSelect(RecoverOperation.Run));
			menu.AddChoice(ExtractItem.SetActionOnSelect(RunExtract));
			menu.AddChoice(SplitItem.SetActionOnSelect(RunSplit));
			menu.AddChoice(JoinItem.SetActionOnSelect(RunJoin));
			menu.AddChoice(BuildItem.SetActionOnSelect(RunBuild));
			menu.AddChoice(new ConsoleMenuItem("Run       install the master into the game and start it").SetActionOnSelect(RunOperation.Run));
			menu.AddChoice(new ConsoleMenuItem("Back"));
			ExtractItem.AddOnKeyPressAction(ToggleExtractStartOver);
			SplitItem.AddOnKeyPressAction(ToggleSplitStartOver);
			JoinItem.AddOnKeyPressAction(ToggleJoinBackUp);
			BuildItem.AddOnKeyPressAction(ToggleBuildBackUp);
			menu.GetChoice();
			ExtractItem.RemoveOnKeyPressAction(ToggleExtractStartOver);
			SplitItem.RemoveOnKeyPressAction(ToggleSplitStartOver);
			JoinItem.RemoveOnKeyPressAction(ToggleJoinBackUp);
			BuildItem.RemoveOnKeyPressAction(ToggleBuildBackUp);
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
			ExtractItem.SetText("Extract   master -> extract\\     <- start over: " + StartOverWord(ExtractStartOver) + " ->");
			SplitItem.SetText("Split     extract\\ -> split\\     <- start over: " + StartOverWord(SplitStartOver) + " ->");
			JoinItem.SetText("Join      split\\ -> extract\\     <- back up first: " + BackUpWord(JoinBackUp) + " ->");
			BuildItem.SetText("Build     extract\\ -> master     <- back up first: " + BackUpWord(BuildBackUp) + " ->");
		}


		private static string StartOverWord(bool startOver) {
			string word = "no";
			if (startOver == true) {
				word = "YES - discards the split";
			}
			return word;
		}


		private static string BackUpWord(bool backUp) {
			string word = "no";
			if (backUp == true) {
				word = "YES";
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


		/// <summary>Left or Right on Join flips its own switch.</summary>
		private static void ToggleJoinBackUp(ConsoleMenuItem? item, ConsoleKeyInfo key) {
			if (IsSideways(key) == true) {
				JoinBackUp = JoinBackUp == false;
			}
		}


		/// <summary>Left or Right on Build flips its own switch.</summary>
		private static void ToggleBuildBackUp(ConsoleMenuItem? item, ConsoleKeyInfo key) {
			if (IsSideways(key) == true) {
				BuildBackUp = BuildBackUp == false;
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


		private static void RunJoin() {
			JoinOperation.Run(JoinBackUp);
			JoinBackUp = false;
		}


		private static void RunBuild() {
			BuildOperation.Run(BuildBackUp);
			BuildBackUp = false;
		}
	}
}
