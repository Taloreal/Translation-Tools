// File: SelectorRow.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The checkpoint selector row: "&lt;- label · state · game · writable   i/n -&gt;", turned
	/// with Left and Right. The main menu's first row, and the Checkpoints menu's, so the
	/// selection can be changed wherever a checkpoint is being looked at.
	/// </summary>
	public static class SelectorRow {

		/// <summary>
		/// The row's text for the selected checkpoint, focusing it on the way.
		/// </summary>
		/// <param name="emptyText">What to show when there is no checkpoint.</param>
		public static string Text(string emptyText) {
			string text = emptyText;
			Checkpoint? selected = CheckpointList.Selected();
			CheckpointWatch.Focus(selected);
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
				text = "<- " + selected.Label
					+ "  ·  " + CheckpointWatch.State.Describe()
					+ game
					+ "  ·  " + selected.StateWord
					+ "   " + (IndexOfSelected() + 1) + "/" + count + " ->";
			}
			return text;
		}


		/// <summary>
		/// Left and Right on the row move the selection through the list.
		/// </summary>
		public static void Cycle(ConsoleMenuItem? item, ConsoleKeyInfo key) {
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
