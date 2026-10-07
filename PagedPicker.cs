// File: PagedPicker.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// A selection menu over a long list, a page at a time: a "&lt;- Page x/y -&gt;" row that
	/// Left and Right turn, one row per item on the page that picks it on Enter, and Back.
	/// The old tool's file picker, kept as the one way a long list is shown here.
	/// </summary>
	public static class PagedPicker {

		/// <summary>Rows per page.</summary>
		public const int PageSize = 10;


		/// <summary>
		/// Shows the rows and returns the index picked, or -1 for Back or an empty list.
		/// </summary>
		/// <param name="rows">One line of text per item.</param>
		/// <param name="title">Printed above the page.</param>
		/// <returns>The picked row's index, or -1.</returns>
		public static int Pick(List<string> rows, string title) {
			int picked = -1;
			if (rows.Count == 0) {
				Console.WriteLine("Nothing to list.");
				ConsoleExt.WaitForEnter("continue");
			}
			if (rows.Count > 0) {
				int pages = (rows.Count + PageSize - 1) / PageSize;
				int page = 0;
				// A list that fits on one page gets exactly its rows and no page row; a
				// longer one gets a full page of slots, blank past the end on the last page.
				int slotCount = Math.Min(PageSize, rows.Count);
				List<ConsoleMenuItem> slots = new();
				for (int at = 0; at < slotCount; at++) {
					int slot = at;
					ConsoleMenuItem item = new("");
					item.SetActionOnSelect(() => {
						int index = page * PageSize + slot;
						if (index < rows.Count) {
							picked = index;
						}
					});
					slots.Add(item);
				}
				ConsoleMenuItem pageRow = new("");
				Action refresh = () => {
					pageRow.SetText("<- Page " + (page + 1) + "/" + pages + " ->");
					for (int at = 0; at < slotCount; at++) {
						int index = page * PageSize + at;
						string text = "";
						if (index < rows.Count) {
							text = rows[index];
						}
						slots[at].SetText(text);
					}
				};
				pageRow.AddOnKeyPressAction((item, key) => {
					int step = 0;
					if (key.Key == ConsoleKey.LeftArrow) {
						step = -1;
					}
					if (key.Key == ConsoleKey.RightArrow) {
						step = 1;
					}
					if (step != 0) {
						page = ((page + step) % pages + pages) % pages;
						refresh();
					}
				});
				refresh();
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText(title + "  (" + rows.Count + ")");
				if (pages > 1) {
					menu.AddChoice(pageRow);
				}
				foreach (ConsoleMenuItem slot in slots) {
					menu.AddChoice(slot);
				}
				menu.AddChoice(new ConsoleMenuItem("Back"));
				menu.GetChoice();
			}
			return picked;
		}
	}
}
