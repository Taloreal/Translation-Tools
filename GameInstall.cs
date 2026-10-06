// File: GameInstall.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// One installed game this machine can run: its folder, the exe that starts it, and
	/// what that exe takes on its command line. The folder is the identity. The engine is
	/// never stored; it is read off the folder when the install is used, since the folder
	/// is the user's and may change under the tool.
	///
	/// Stored as named lines, like a checkpoint, so fields can be added later and lines
	/// this build does not recognise survive a save.
	/// </summary>
	public class GameInstall {

		private const string FolderName = "Folder";
		private const string LauncherName = "Launcher";
		private const string ArgumentsName = "Arguments";


		/// <summary>
		/// Rebuilds an install from the text Encode wrote.
		/// </summary>
		/// <param name="encoded">The stored text.</param>
		/// <param name="install">The install, complete only when this returns true.</param>
		/// <returns>True when the text held a folder.</returns>
		public static bool TryDecode(string encoded, out GameInstall install) {
			install = new GameInstall();
			foreach (string line in encoded.Split('\n')) {
				int equals = line.IndexOf('=');
				if (equals > 0) {
					string name = line.Substring(0, equals);
					string value = line.Substring(equals + 1);
					bool known = false;
					if (name == FolderName) {
						install.Folder = value;
						known = true;
					}
					if (name == LauncherName) {
						install.Launcher = value;
						known = true;
					}
					if (name == ArgumentsName) {
						install.Arguments = value;
						known = true;
					}
					if (known == false) {
						install.UnknownLines.Add(line);
					}
				}
			}
			return install.Folder.Length > 0;
		}


		/// <summary>
		/// Whether two folders are the same install: equal ignoring case and a trailing slash.
		/// </summary>
		public static bool SameFolder(string first, string second) {
			return string.Equals(first.TrimEnd('\\', '/'), second.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
		}


		/// <summary>The installed game's folder: where its live archive and engine live.</summary>
		public string Folder = "";

		/// <summary>The exe Run starts. Inside the folder, or the bundled engine.</summary>
		public string Launcher = "";

		/// <summary>What Run passes the launcher, e.g. an engine name for a locale bypass. Empty for none.</summary>
		public string Arguments = "";

		/// <summary>Lines from a newer build that this one does not understand, carried through unchanged.</summary>
		public List<string> UnknownLines = new();

		/// <summary>The folder's own name, for a row.</summary>
		public string Name {
			get { return Path.GetFileName(Folder.TrimEnd('\\', '/')); }
		}


		/// <summary>
		/// The text TryDecode reads back: one named field per line.
		/// </summary>
		public string Encode() {
			string encoded = FolderName + "=" + Folder + "\n"
				+ LauncherName + "=" + Launcher + "\n"
				+ ArgumentsName + "=" + Arguments;
			foreach (string line in UnknownLines) {
				encoded += "\n" + line;
			}
			return encoded;
		}
	}
}
