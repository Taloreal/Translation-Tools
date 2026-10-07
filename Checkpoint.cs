// File: Checkpoint.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// One split tree (or packed archive) the tool holds a reference to. The label is what
	/// the user knows it by and may change; the serial (its creation second) is its identity
	/// for anything that refers to it; the path can be re-pointed. Engine and split state are never stored here -
	/// they are read off the disk when the checkpoint is shown or used. The game it is lives
	/// in checkpoint.info inside the folder (see CheckpointInfo), not here: this entry holds
	/// what is true of this machine, the file holds what is true of the folder.
	///
	/// Stored as named lines ("Label=...", "Path=...") rather than by position, so fields can
	/// be added later without breaking what is already saved. Lines this build does not
	/// recognise are kept and written back, so a newer build's fields survive a save by an
	/// older one.
	/// </summary>
	public class Checkpoint {

		private const string LabelName = "Label";
		private const string PathName = "Path";
		private const string WritableName = "Writable";
		private const string BuildModeName = "BuildMode";
		private const string CompilerVersionName = "CompilerVersion";
		private const string WarningName = "Warning";
		private const string WrapColumnName = "WrapColumn";
		private const string GameFolderName = "GameFolder";
		private const string SerialName = "Serial";

		/// <summary>The BuildMode value meaning "none recorded".</summary>
		public const int NoBuildMode = -1;

		/// <summary>How a serial is written: the creation moment to the second, as fourteen digits.</summary>
		public const string SerialFormat = "yyyyMMddHHmmss";

		/// <summary>The WrapColumn value meaning "no governing column".</summary>
		public const int NoWrapColumn = 0;


		/// <summary>
		/// Rebuilds a checkpoint from the text Encode wrote.
		/// </summary>
		/// <param name="encoded">The stored text.</param>
		/// <param name="checkpoint">The checkpoint, complete only when this returns true.</param>
		/// <returns>True when the text held at least a label and a path.</returns>
		public static bool TryDecode(string encoded, out Checkpoint checkpoint) {
			checkpoint = new Checkpoint();
			string[] lines = encoded.Split('\n');
			foreach (string line in lines) {
				int equals = line.IndexOf('=');
				if (equals > 0) {
					string name = line.Substring(0, equals);
					string value = line.Substring(equals + 1);
					bool known = false;
					if (name == LabelName) {
						checkpoint.Label = value;
						known = true;
					}
					if (name == PathName) {
						checkpoint.Path = value;
						known = true;
					}
					if (name == WritableName) {
						checkpoint.Writable = value == "true";
						known = true;
					}
					if (name == "GameName" || name == "VndbId") {
						// Older builds kept these here; they now live in checkpoint.info. Dropped.
						known = true;
					}
					if (name == BuildModeName) {
						bool parsed = int.TryParse(value, out int mode);
						checkpoint.BuildMode = parsed == true
							? mode
							: NoBuildMode;
						known = true;
					}
					if (name == CompilerVersionName) {
						checkpoint.CompilerVersion = value;
						known = true;
					}
					if (name == WarningName) {
						checkpoint.Warning = value;
						known = true;
					}
					if (name == WrapColumnName) {
						bool parsedColumn = int.TryParse(value, out int column);
						checkpoint.WrapColumn = parsedColumn == true && column > 0
							? column
							: NoWrapColumn;
						known = true;
					}
					if (name == GameFolderName) {
						checkpoint.GameFolder = value;
						known = true;
					}
					if (name == SerialName) {
						checkpoint.Serial = value.Trim();
						known = true;
					}
					if (name == "Launcher" || name == "LauncherArguments") {
						// Older builds kept these here; they now live on the game install. Dropped.
						known = true;
					}
					if (known == false) {
						checkpoint.UnknownLines.Add(line);
					}
				}
			}
			return checkpoint.Label.Length > 0 && checkpoint.Path.Length > 0;
		}


		/// <summary>
		/// The serial a checkpoint created at a moment gets: that moment to the second.
		/// </summary>
		/// <param name="moment">When the checkpoint is created.</param>
		/// <returns>Fourteen digits.</returns>
		public static string SerialAt(DateTime moment) {
			return moment.ToString(SerialFormat, System.Globalization.CultureInfo.InvariantCulture);
		}


		/// <summary>
		/// True for text that may be a label or a path: not empty, and no line break, since
		/// the stored form is one field per line.
		/// </summary>
		/// <param name="value">The text to check.</param>
		/// <returns>Whether the text can be stored.</returns>
		public static bool IsStorable(string value) {
			return value.Length > 0 && value.Contains('\n') == false && value.Contains('\r') == false;
		}


		/// <summary>The user's unique name for this checkpoint.</summary>
		public string Label = "";

		/// <summary>The folder (or archive file) on disk.</summary>
		public string Path = "";

		/// <summary>Whether the tool may write into it. False means locked.</summary>
		public bool Writable;

		/// <summary>The build mode found for a Siglus master, or NoBuildMode. Shown to the user only as the game name.</summary>
		public int BuildMode = NoBuildMode;

		/// <summary>The compiler that verified the build mode, e.g. "siglus-ssu 0.5.4". Empty until then.</summary>
		public string CompilerVersion = "";

		/// <summary>A standing caution about this checkpoint, shown on its row until cleared. Empty when there is none.</summary>
		public string Warning = "";

		/// <summary>Siglus only: the display column Join wraps prose at where a scene carries no wrap control code. NoWrapColumn for none.</summary>
		public int WrapColumn = NoWrapColumn;

		/// <summary>The folder of the game install Run uses (see GameInstallList), which holds the launcher. Empty until Run has asked.</summary>
		public string GameFolder = "";

		/// <summary>
		/// The checkpoint's identity for anything that refers to it from outside the list, such
		/// as an alignment: the moment it was created, to the second, as fourteen digits. The
		/// label is the user's to read and change; the serial never changes. Empty only on an
		/// entry written by a build that had no serials, until the list stamps it.
		/// </summary>
		public string Serial = "";

		/// <summary>Whether a build mode has been recorded.</summary>
		public bool HasBuildMode {
			get { return BuildMode != NoBuildMode; }
		}

		/// <summary>Lines from a newer build that this one does not understand, carried through unchanged.</summary>
		public List<string> UnknownLines = new();

		/// <summary>"writable" or "locked", for headers and rows.</summary>
		public string StateWord {
			get {
				string word = "locked";
				if (Writable == true) {
					word = "writable";
				}
				return word;
			}
		}


		/// <summary>
		/// The text TryDecode reads back: one named field per line.
		/// </summary>
		/// <returns>The stored form.</returns>
		public string Encode() {
			string writable = "false";
			if (Writable == true) {
				writable = "true";
			}
			string encoded = LabelName + "=" + Label + "\n"
				+ PathName + "=" + Path + "\n"
				+ WritableName + "=" + writable + "\n"
				+ BuildModeName + "=" + BuildMode + "\n"
				+ CompilerVersionName + "=" + CompilerVersion + "\n"
				+ WarningName + "=" + Warning + "\n"
				+ WrapColumnName + "=" + WrapColumn + "\n"
				+ GameFolderName + "=" + GameFolder + "\n"
				+ SerialName + "=" + Serial;
			foreach (string line in UnknownLines) {
				encoded += "\n" + line;
			}
			return encoded;
		}
	}
}
