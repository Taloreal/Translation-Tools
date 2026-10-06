// File: BundledEngine.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// The NScripter engine the tool ships: ONScripter-EN, in Tools\ONScripter beside the
	/// exe (or in the repo's Tools\ when run from a build folder). Offered as a launcher for
	/// any NScripter checkpoint, so a game folder needs no engine of its own; it is started
	/// with the game folder as its working directory and nothing is copied into the game.
	/// ONScripter-EN is GPL; its license ships in the same folder.
	/// </summary>
	public static class BundledEngine {

		private const string Folder = "ONScripter";
		private const string ExeName = "onscripter-en.exe";

		/// <summary>The label Run shows for this launcher.</summary>
		public const string Label = "Use the bundled ONScripter-EN";

		private static string FoundExe = "";
		private static bool Searched = false;

		/// <summary>Where the bundled engine is, or empty when it did not ship with this copy.</summary>
		public static string ExePath {
			get {
				if (Searched == false) {
					Searched = true;
					FoundExe = Find();
				}
				return FoundExe;
			}
		}

		/// <summary>Whether the bundled engine is present.</summary>
		public static bool Available {
			get { return ExePath.Length > 0; }
		}


		private static string Find() {
			string found = "";
			string[] candidates = new string[] {
				Path.Combine(AppContext.BaseDirectory, "Tools", Folder, ExeName),
				Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Tools", Folder, ExeName),
			};
			foreach (string candidate in candidates) {
				if (found.Length == 0 && File.Exists(candidate) == true) {
					found = Path.GetFullPath(candidate);
				}
			}
			return found;
		}
	}
}
