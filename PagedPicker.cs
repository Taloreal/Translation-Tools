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
		/// <param name="backLabel">What the last row says; "Back" unless leaving means something more specific, like "Do not copy".</param>
		/// <returns>The picked row's index, or -1.</returns>
		public static int Pick(List<string> rows, string title, string backLabel = "Back") {
			return Pick(new List<string>(), 0, (mode) => rows, title, out int unused, backLabel);
		}


		/// <summary>
		/// Shows the rows in one of several orders and returns the index picked within the
		/// order shown, or -1 for Back or an empty list. A "Sort: &lt;- mode -&gt;" row above
		/// the list flips the order on Left, Right or Enter and returns to page one. Every
		/// order lists the same items; only their places differ.
		/// </summary>
		/// <param name="modes">The sort row's words, one per order; empty for no sort row.</param>
		/// <param name="mode">The order to open in.</param>
		/// <param name="rowsFor">The rows for an order.</param>
		/// <param name="title">Printed above the page.</param>
		/// <param name="pickedMode">The order shown when the pick was made or Back was chosen.</param>
		/// <param name="backLabel">What the last row says.</param>
		/// <returns>The picked row's index in the order shown, or -1.</returns>
		public static int Pick(List<string> modes, int mode, Func<int, List<string>> rowsFor, string title, out int pickedMode, string backLabel = "Back") {
			int picked = -1;
			List<string> rows = rowsFor(mode);
			if (rows.Count == 0) {
				Console.WriteLine("Nothing to list.");
				ConsoleExt.WaitForEnter("continue");
			}
			bool showing = rows.Count > 0;
			while (showing == true) {
				// Enter on the sort row flips the order and shows the list again; a pick or
				// Back ends it. Left and Right on the sort row flip it in place.
				showing = false;
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
				ConsoleMenuItem sortRow = new("");
				Action refresh = () => {
					pageRow.SetText("<- Page " + (page + 1) + "/" + pages + " ->");
					if (modes.Count > 0) {
						sortRow.SetText("Sort: <- " + modes[mode] + " ->");
					}
					for (int at = 0; at < slotCount; at++) {
						int index = page * PageSize + at;
						string text = "";
						if (index < rows.Count) {
							text = rows[index];
						}
						slots[at].SetText(text);
					}
				};
				Action flip = () => {
					mode = (mode + 1) % modes.Count;
					rows = rowsFor(mode);
					page = 0;
					refresh();
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
				sortRow.AddOnKeyPressAction((item, key) => {
					if (OperationsMenu.IsSideways(key) == true) {
						flip();
					}
				});
				sortRow.SetActionOnSelect(() => {
					flip();
					showing = true;
				});
				refresh();
				ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
				menu.SetPreChoiceText(title + "  (" + rows.Count + ")");
				if (modes.Count > 0) {
					menu.AddChoice(sortRow);
				}
				if (pages > 1) {
					menu.AddChoice(pageRow);
				}
				foreach (ConsoleMenuItem slot in slots) {
					menu.AddChoice(slot);
				}
				menu.AddChoice(new ConsoleMenuItem(backLabel));
				menu.GetChoice();
			}
			pickedMode = mode;
			return picked;
		}
	}
}
