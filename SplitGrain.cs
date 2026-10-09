// File: SplitGrain.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// How finely Split cuts a script: one dialogue file per function or label, the grain a
	/// dense game like TGD reads well at, or one dialogue file for the whole script (one per
	/// scene file on Siglus), for a sparse game whose functions hold a line or three each
	/// and give a translator no context on their own. Two grains and no middle: functions
	/// have no logical grouping between those two, and gathering unrelated ones by count
	/// would read worse than either. A Settings switch, read at split time and written
	/// into the checkpoint's info so a later look says how its split was cut.
	/// </summary>
	public static class SplitGrain {

		private const string WholeKey = "Split.WholeScript";

		/// <summary>The info-file word for one file per function or label.</summary>
		public const string FunctionWord = "function";

		/// <summary>The info-file word for the whole script, or scene, in one file.</summary>
		public const string WholeWord = "whole";


		/// <summary>Whether Split cuts the whole script into one pair of files; off is one per function.</summary>
		public static bool WholeScript {
			get {
				bool whole = false;
				bool stored = Settings.GetValue(WholeKey, out bool saved);
				if (stored == true) {
					whole = saved;
				}
				return whole;
			}
			set { Settings.SetValue(WholeKey, value); }
		}


		/// <summary>The current grain, as the info file records it.</summary>
		public static string Word {
			get {
				string word = FunctionWord;
				if (WholeScript == true) {
					word = WholeWord;
				}
				return word;
			}
		}


		/// <summary>
		/// One line for a menu row.
		/// </summary>
		public static string Describe() {
			string text = "per function";
			if (WholeScript == true) {
				text = "whole script";
			}
			return text;
		}
	}
}
