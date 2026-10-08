// File: YesNoMenu.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The one way the tool asks a yes-or-no question: a menu with a single row that Left
	/// and Right flip between no and yes, Enter taking what it shows, plus a Cancel row when
	/// cancelling means something other than no. The row starts at no, so Enter alone never
	/// says yes to anything, except where the caller says yes is the harmless, expected
	/// answer. Replaces typed (y/n) prompts everywhere.
	/// </summary>
	public static class YesNoMenu {

		/// <summary>
		/// Asks a question whose answers are yes and no.
		/// </summary>
		/// <param name="question">The question, without a trailing prompt.</param>
		/// <param name="context">Lines to show above the question; empty for none.</param>
		/// <param name="startYes">True to start the row at yes: only for a question where yes is harmless and expected, never for one that destroys anything.</param>
		/// <returns>True for yes.</returns>
		public static bool Ask(string question, string context = "", bool startYes = false) {
			bool cancelled = false;
			return Show(question, context, false, startYes, out cancelled);
		}


		/// <summary>
		/// Asks a question whose answers are yes, no, and cancel.
		/// </summary>
		/// <param name="question">The question, without a trailing prompt.</param>
		/// <param name="cancelled">True when the user chose Cancel; the result is then false.</param>
		/// <param name="context">Lines to show above the question; empty for none.</param>
		/// <returns>True for yes.</returns>
		public static bool AskOrCancel(string question, out bool cancelled, string context = "") {
			return Show(question, context, true, false, out cancelled);
		}


		private static bool Show(string question, string context, bool allowCancel, bool startYes, out bool cancelled) {
			bool yes = startYes;
			bool wasCancelled = false;
			ConsoleSelectMenu menu = new(loops: false, numbered: false, clearOnRefresh: true);
			string above = "";
			if (context.Length > 0) {
				above = context.TrimEnd('\n', '\r') + "\n\n";
			}
			menu.SetPreChoiceText(above);
			ConsoleMenuItem row = new("");
			Action refresh = () => {
				string word = "no";
				if (yes == true) {
					word = "YES";
				}
				row.SetText("<- " + question + "  " + word + " ->");
			};
			row.AddOnKeyPressAction((item, key) => {
				if (key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow) {
					yes = yes == false;
					refresh();
				}
			});
			refresh();
			menu.AddChoice(row);
			if (allowCancel == true) {
				menu.AddChoice(new ConsoleMenuItem("Cancel").SetActionOnSelect(() => { wasCancelled = true; }));
			}
			menu.GetChoice();
			cancelled = wasCancelled;
			if (wasCancelled == true) {
				yes = false;
			}
			return yes;
		}
	}
}
