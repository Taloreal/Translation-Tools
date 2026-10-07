// File: ChoiceLocations.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// One choice block as the split found it: which dialogue file holds its options, which
	/// command it is, the index range the options occupy, and how many there are.
	/// </summary>
	public class ChoiceLocation {

		/// <summary>The dialogue file's key: its name without folder or extension.</summary>
		public string FileKey = "";

		/// <summary>The command, e.g. "select" or "selbtn".</summary>
		public string Command = "";

		/// <summary>The first option's index in that file.</summary>
		public int FirstIndex = 0;

		/// <summary>The last option's index in that file.</summary>
		public int LastIndex = 0;

		/// <summary>How many option lines the block holds.</summary>
		public int Options = 0;
	}


	/// <summary>
	/// checkpoint.choices: where every choice the player is offered lives in the split,
	/// written fresh by each Split at the top of the checkpoint so a translator can go
	/// straight to them. One line per block, in script order:
	///
	///   menu    select    ::0000000001::-::0000000002::    2 options
	///
	/// The inspector ignores the file; Fork carries it.
	/// </summary>
	public static class ChoiceLocations {

		/// <summary>The file's name, allowed at the top level of every checkpoint.</summary>
		public const string FileName = "checkpoint.choices";


		/// <summary>
		/// Writes the file whole. No blocks means an empty file, which still says "none".
		/// </summary>
		/// <param name="checkpointFolder">The checkpoint's folder.</param>
		/// <param name="locations">The blocks, in script order.</param>
		public static void Write(string checkpointFolder, List<ChoiceLocation> locations) {
			StringBuilder text = new();
			foreach (ChoiceLocation location in locations) {
				string range = NScripterSplit.PointerFor(location.FirstIndex);
				if (location.LastIndex != location.FirstIndex) {
					range += "-" + NScripterSplit.PointerFor(location.LastIndex);
				}
				text.Append(location.FileKey.PadRight(24)).Append(location.Command.PadRight(10))
					.Append(range.PadRight(32)).Append(location.Options).Append(" option(s)").AppendLine();
			}
			File.WriteAllText(Path.Combine(checkpointFolder, FileName), text.ToString(), new UTF8Encoding(false));
		}


		/// <summary>
		/// Reads the file back. A missing file is an empty list.
		/// </summary>
		public static List<ChoiceLocation> Read(string checkpointFolder) {
			List<ChoiceLocation> locations = new();
			string path = Path.Combine(checkpointFolder, FileName);
			if (File.Exists(path) == true) {
				foreach (string line in File.ReadAllLines(path)) {
					string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length >= 4) {
						ChoiceLocation location = new();
						location.FileKey = parts[0];
						location.Command = parts[1];
						string[] ends = parts[2].Split('-');
						if (NScripterSplit.TryReadPointer(ends[0], out string first, out int firstIndex, out string rest) == true) {
							location.FirstIndex = firstIndex;
							location.LastIndex = firstIndex;
						}
						if (ends.Length > 1 && NScripterSplit.TryReadPointer(ends[1], out string last, out int lastIndex, out string restLast) == true) {
							location.LastIndex = lastIndex;
						}
						int.TryParse(parts[3], out location.Options);
						locations.Add(location);
					}
				}
			}
			return locations;
		}
	}
}
