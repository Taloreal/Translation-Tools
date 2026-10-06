// File: SiglusCompiler.cs
// Namespace: TranslationTools
using System.Diagnostics;

namespace TranslationTools {

	/// <summary>
	/// The tool's side of the machine-wide siglus-ssu install. Finds the exe the way
	/// Scripts\Update-SiglusSsu.ps1 placed it - through the Python launcher, in the newest
	/// installed Python's user scripts folder - and runs its modes with every line of
	/// output handed to the caller as it arrives. Nothing here assumes a version or a path.
	/// </summary>
	public static class SiglusCompiler {

		private const string ExeName = "siglus-ssu.exe";
		private const string PythonLauncher = "py";

		/// <summary>The build mode most Siglus titles use; the compiler's own default.</summary>
		public const int StandardMode = 0;

		private static string FoundExe = "";
		private static bool Searched = false;

		/// <summary>Where siglus-ssu.exe is, or empty when it is not installed.</summary>
		public static string ExePath {
			get {
				if (Searched == false) {
					Searched = true;
					FoundExe = FindExe();
				}
				return FoundExe;
			}
		}

		/// <summary>Whether the compiler is installed.</summary>
		public static bool Available {
			get { return ExePath.Length > 0; }
		}

		/// <summary>What to tell a user who has no compiler and declined to install it.</summary>
		public const string NotInstalledMessage = "siglus-ssu is not installed. Run Scripts\\Update-SiglusSsu.bat, then try again.";

		private const string InstallerScript = "Update-SiglusSsu.ps1";


		/// <summary>
		/// Runs the shipped installer script (Scripts\Update-SiglusSsu.ps1), which finds or
		/// installs a suitable Python and then installs or updates siglus-ssu, streaming its
		/// output. Afterwards the exe is looked for again.
		/// </summary>
		/// <param name="onLine">Receives every line the installer prints.</param>
		/// <returns>True when the compiler is available afterwards.</returns>
		public static bool Install(Action<string> onLine) {
			string script = FindInstaller();
			if (script.Length == 0) {
				onLine("The installer script " + InstallerScript + " is not beside the tool.");
			}
			if (script.Length > 0) {
				int exitCode = -1;
				try {
					ProcessStartInfo start = new("powershell", "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script));
					start.UseShellExecute = false;
					start.RedirectStandardOutput = true;
					start.RedirectStandardError = true;
					start.CreateNoWindow = true;
					using (Process process = new()) {
						process.StartInfo = start;
						process.OutputDataReceived += (sender, received) => { if (received.Data != null) { onLine(received.Data); } };
						process.ErrorDataReceived += (sender, received) => { if (received.Data != null) { onLine(received.Data); } };
						process.Start();
						process.BeginOutputReadLine();
						process.BeginErrorReadLine();
						process.WaitForExit();
						exitCode = process.ExitCode;
					}
				}
				catch (Exception exception) {
					onLine("Could not run the installer: " + exception.Message);
				}
				if (exitCode != 0) {
					onLine("The installer stopped with code " + exitCode + ".");
				}
			}
			Searched = false;
			return Available;
		}


		/// <summary>
		/// The installer script: in Scripts\ beside the exe when shipped, or in the repo's
		/// Scripts\ when run from a build folder.
		/// </summary>
		private static string FindInstaller() {
			string found = "";
			string[] candidates = new string[] {
				Path.Combine(AppContext.BaseDirectory, "Scripts", InstallerScript),
				Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Scripts", InstallerScript),
			};
			foreach (string candidate in candidates) {
				if (found.Length == 0 && File.Exists(candidate) == true) {
					found = Path.GetFullPath(candidate);
				}
			}
			return found;
		}


		/// <summary>
		/// The compiler's version line, e.g. "siglus-ssu 0.5.4"; empty when not installed.
		/// </summary>
		public static string Version() {
			string version = "";
			if (Available == true) {
				List<string> lines = new();
				Run("--version", lines.Add);
				if (lines.Count > 0) {
					version = lines[0].Trim();
				}
			}
			return version;
		}


		/// <summary>
		/// One build mode the installed compiler offers, by the game it is for.
		/// </summary>
		public class KnownMode {
			public int Number;
			public string Game = "";
		}


		/// <summary>
		/// The build modes the installed compiler names a game for, read from its own help so
		/// the list is never stale. The help line reads, in one piece:
		///   --const-profile Select const profile (0, 1, 2, 3; default: 0; 3 supports X; 4 supports Y; ...)
		/// Only modes that name a game are listed; the standard mode is the caller's to add.
		/// </summary>
		/// <returns>The named modes, in the compiler's order; empty when the compiler is missing or its help changed.</returns>
		public static List<KnownMode> KnownModes() {
			List<KnownMode> modes = new();
			if (Available == true) {
				List<string> lines = new();
				Run("--help", lines.Add);
				string profileLine = "";
				foreach (string line in lines) {
					if (profileLine.Length == 0 && line.Trim().StartsWith("--const-profile") == true) {
						profileLine = line;
					}
				}
				int open = profileLine.IndexOf('(');
				int close = profileLine.LastIndexOf(')');
				if (open >= 0 && close > open) {
					string inside = profileLine.Substring(open + 1, close - open - 1);
					foreach (string piece in inside.Split(';')) {
						string text = piece.Trim();
						int at = text.IndexOf(" supports ");
						if (at > 0 && int.TryParse(text.Substring(0, at).Trim(), out int number) == true) {
							KnownMode mode = new();
							mode.Number = number;
							mode.Game = text.Substring(at + " supports ".Length).Trim();
							modes.Add(mode);
						}
					}
				}
			}
			return modes;
		}


		/// <summary>
		/// Lets the compiler find an archive's build mode by round-tripping it: extract,
		/// rebuild under each mode in turn, compare with the original. Minutes, not seconds.
		/// </summary>
		/// <param name="archivePath">The Scene.pck to test; only read.</param>
		/// <param name="onLine">Receives every line the compiler prints.</param>
		/// <param name="buildMode">The mode it settled on, or Checkpoint.NoBuildMode.</param>
		/// <returns>True when the archive was reproduced (EXACT or PAYLOAD_SAME).</returns>
		public static bool FindBuildMode(string archivePath, Action<string> onLine, out int buildMode) {
			buildMode = Checkpoint.NoBuildMode;
			string verdict = "";
			int foundMode = Checkpoint.NoBuildMode;

			// The compiler ends with:  result: PAYLOAD_SAME profile=3 same=155 ...
			void Watch(string line) {
				onLine(line);
				string text = line.Trim();
				if (text.StartsWith("result:") == true) {
					string[] tokens = text.Substring(7).Trim().Split(' ');
					if (tokens.Length > 0) {
						verdict = tokens[0];
					}
					foreach (string token in tokens) {
						if (token.StartsWith("profile=") == true) {
							int.TryParse(token.Substring(8), out foundMode);
						}
					}
				}
			}

			Run("test " + Quote(archivePath), Watch);
			bool reproduced = verdict == "EXACT" || verdict == "PAYLOAD_SAME";
			if (reproduced == true) {
				buildMode = foundMode;
			}
			return reproduced;
		}


		/// <summary>
		/// Extracts an archive's sources into a folder under a build mode. The compiler
		/// writes into output_&lt;stamp&gt;\ beneath the destination; that is flattened up so
		/// the destination itself holds the sources.
		/// </summary>
		/// <param name="archivePath">The Scene.pck; only read.</param>
		/// <param name="destination">The folder to fill; created if missing.</param>
		/// <param name="buildMode">The mode the archive was built with.</param>
		/// <param name="onLine">Receives every line the compiler prints.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Extract(string archivePath, string destination, int buildMode, Action<string> onLine) {
			string problem = "";
			Directory.CreateDirectory(destination);
			int exitCode = Run("--const-profile " + buildMode + " -x " + Quote(archivePath) + " " + Quote(destination), onLine);
			if (exitCode != 0) {
				problem = "The compiler stopped with code " + exitCode + " while extracting.";
			}
			if (problem.Length == 0) {
				problem = FlattenOutput(destination);
			}
			return problem;
		}


		/// <summary>
		/// Compiles a folder of sources into an archive under a build mode. The output path
		/// is written only by the compiler; the caller decides what to do with it.
		/// </summary>
		/// <param name="sourceFolder">The folder holding the .ss files and their support files.</param>
		/// <param name="outputArchive">The .pck to write.</param>
		/// <param name="buildMode">The mode the game was built with.</param>
		/// <param name="onLine">Receives every line the compiler prints.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string Compile(string sourceFolder, string outputArchive, int buildMode, Action<string> onLine) {
			string problem = "";
			int exitCode = Run("--const-profile " + buildMode + " -c " + Quote(sourceFolder) + " " + Quote(outputArchive), onLine);
			if (exitCode != 0) {
				problem = "The compiler stopped with code " + exitCode + ".";
			}
			if (problem.Length == 0 && File.Exists(outputArchive) == false) {
				problem = "The compiler finished but wrote no archive.";
			}
			return problem;
		}


		/// <summary>
		/// Moves the contents of the single output_* folder the compiler made up into its
		/// parent, and removes the empty folder.
		/// </summary>
		private static string FlattenOutput(string destination) {
			string problem = "";
			List<string> stamped = new();
			foreach (string folder in Directory.EnumerateDirectories(destination)) {
				if (Path.GetFileName(folder).StartsWith("output_", StringComparison.OrdinalIgnoreCase) == true) {
					stamped.Add(folder);
				}
			}
			if (stamped.Count == 0) {
				problem = "The compiler finished but made no output folder.";
			}
			if (stamped.Count > 1) {
				problem = "The destination holds " + stamped.Count + " output folders; expected one.";
			}
			if (stamped.Count == 1) {
				foreach (string entry in Directory.EnumerateFileSystemEntries(stamped[0])) {
					string target = Path.Combine(destination, Path.GetFileName(entry));
					if (Directory.Exists(entry) == true) {
						Directory.Move(entry, target);
					}
					if (File.Exists(entry) == true) {
						File.Move(entry, target, true);
					}
				}
				Directory.Delete(stamped[0]);
			}
			return problem;
		}


		/// <summary>
		/// Runs the compiler with an argument line, streaming its output.
		/// </summary>
		/// <returns>The exit code, or -1 when it could not start.</returns>
		private static int Run(string arguments, Action<string> onLine) {
			int exitCode = -1;
			try {
				ProcessStartInfo start = new(ExePath, arguments);
				start.UseShellExecute = false;
				start.RedirectStandardOutput = true;
				start.RedirectStandardError = true;
				start.CreateNoWindow = true;
				using (Process process = new()) {
					process.StartInfo = start;
					process.OutputDataReceived += (sender, received) => { if (received.Data != null) { onLine(received.Data); } };
					process.ErrorDataReceived += (sender, received) => { if (received.Data != null) { onLine(received.Data); } };
					process.Start();
					process.BeginOutputReadLine();
					process.BeginErrorReadLine();
					process.WaitForExit();
					exitCode = process.ExitCode;
				}
			}
			catch (Exception exception) {
				onLine("Could not run the compiler: " + exception.Message);
			}
			return exitCode;
		}


		/// <summary>
		/// Looks for siglus-ssu.exe in the user scripts folder of every Python the launcher
		/// knows, newest first.
		/// </summary>
		private static string FindExe() {
			string found = "";
			foreach (string version in InstalledPythonVersions()) {
				if (found.Length == 0) {
					string scriptsFolder = Capture(PythonLauncher, "-" + version + " -c \"import sysconfig; print(sysconfig.get_path('scripts', 'nt_user'))\"").Trim();
					if (scriptsFolder.Length > 0) {
						string candidate = Path.Combine(scriptsFolder, ExeName);
						if (File.Exists(candidate) == true) {
							found = candidate;
						}
					}
				}
			}
			return found;
		}


		/// <summary>
		/// The versions the py launcher lists, newest first. "py -0p" prints one per line,
		/// e.g. " -V:3.13 *        C:\...\python.exe".
		/// </summary>
		private static List<string> InstalledPythonVersions() {
			List<string> versions = new();
			string listing = Capture(PythonLauncher, "-0p");
			foreach (string line in listing.Split('\n')) {
				string text = line.Trim();
				if (text.StartsWith("-V:") == true) {
					string tag = text.Substring(3).Split(' ')[0].TrimEnd('*').Trim();
					if (tag.Length > 0) {
						versions.Add(tag);
					}
				}
			}
			versions.Sort(CompareVersionsDescending);
			return versions;
		}


		private static int CompareVersionsDescending(string first, string second) {
			System.Version.TryParse(first, out System.Version? a);
			System.Version.TryParse(second, out System.Version? b);
			int order = 0;
			if (a != null && b != null) {
				order = b.CompareTo(a);
			}
			return order;
		}


		/// <summary>
		/// Runs a program and returns everything it printed; empty when it could not run.
		/// </summary>
		private static string Capture(string program, string arguments) {
			string output = "";
			try {
				ProcessStartInfo start = new(program, arguments);
				start.UseShellExecute = false;
				start.RedirectStandardOutput = true;
				start.RedirectStandardError = true;
				start.CreateNoWindow = true;
				using (Process process = Process.Start(start)!) {
					output = process.StandardOutput.ReadToEnd();
					process.WaitForExit();
				}
			}
			catch (Exception) {
				output = "";
			}
			return output;
		}


		private static string Quote(string path) {
			return "\"" + path + "\"";
		}
	}
}
