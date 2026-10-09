// File: CharacterSort.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// The order the character picker lists the glossary in: by written name, or by how
	/// many lines each character speaks in the selected checkpoint, most first. The choice
	/// is made on the picker's own sort row and saved, so it survives runs; it is not a
	/// Settings row, since it belongs where the list is.
	/// </summary>
	public static class CharacterSort {

		private const string ByLinesKey = "Glossary.SortByLines";

		/// <summary>The sort row's words, in mode order: 0 by name, 1 by lines.</summary>
		public static readonly List<string> Modes = new() { "by name", "by lines" };


		/// <summary>Whether the picker lists the most-spoken character first; off is by written name.</summary>
		public static bool ByLines {
			get {
				bool byLines = false;
				bool stored = Settings.GetValue(ByLinesKey, out bool saved);
				if (stored == true) {
					byLines = saved;
				}
				return byLines;
			}
			set { Settings.SetValue(ByLinesKey, value); }
		}


		/// <summary>The mode index for the saved choice.</summary>
		public static int Mode {
			get {
				int mode = 0;
				if (ByLines == true) {
					mode = 1;
				}
				return mode;
			}
			set { ByLines = value == 1; }
		}


		/// <summary>
		/// The characters in one of the two orders. By name is the written name, ordinal and
		/// case-blind. By lines is most lines first, a character with no count last, ties by
		/// name; with no counts at all it is by name.
		/// </summary>
		/// <param name="entries">The glossary's characters.</param>
		/// <param name="lines">Lines spoken, by written name; may lack a character or be empty.</param>
		/// <param name="mode">0 by name, 1 by lines.</param>
		public static List<CharacterEntry> Order(List<CharacterEntry> entries, Dictionary<string, int> lines, int mode) {
			List<CharacterEntry> ordered = new(entries);
			ordered.Sort((first, second) => {
				int order = 0;
				if (mode == 1) {
					order = LinesOf(second, lines).CompareTo(LinesOf(first, lines));
				}
				if (order == 0) {
					order = string.Compare(first.Written, second.Written, StringComparison.OrdinalIgnoreCase);
				}
				return order;
			});
			return ordered;
		}


		private static int LinesOf(CharacterEntry entry, Dictionary<string, int> lines) {
			int count = -1;
			if (lines.ContainsKey(entry.Written) == true) {
				count = lines[entry.Written];
			}
			return count;
		}
	}
}
