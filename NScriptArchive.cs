// File: NScriptArchive.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// The classic NScripter archive format: nscript.dat is the script with every byte
	/// XORed with 0x84, nothing more. Decoding and encoding are the same operation, so a
	/// decoded script re-encodes to a byte-identical archive. Demonstrated on the real
	/// game both ways on 2026-10-06, replacing crage.exe and nscmake.exe.
	///
	/// The one guard is the same one Crass used: a file whose first four bytes carry no
	/// high bit is already plain text and is refused rather than scrambled.
	/// </summary>
	public static class NScriptArchive {

		/// <summary>The archive's file name, by NScripter's convention.</summary>
		public const string ArchiveName = "nscript.dat";

		/// <summary>The decoded script's file name, by NScripter's convention.</summary>
		public const string ScriptName = "0.txt";

		private const byte Key = 0x84;


		/// <summary>
		/// Whether bytes look like an encoded archive rather than plain text: at least one
		/// of the first four bytes has its high bit set.
		/// </summary>
		/// <param name="data">The file's bytes.</param>
		/// <returns>True when decoding makes sense.</returns>
		public static bool LooksEncoded(byte[] data) {
			bool anyHighBit = false;
			int checkLength = Math.Min(4, data.Length);
			for (int index = 0; index < checkLength; index++) {
				if ((data[index] & 0x80) != 0) {
					anyHighBit = true;
				}
			}
			return anyHighBit;
		}


		/// <summary>
		/// The script text behind an archive's bytes.
		/// </summary>
		/// <param name="archive">The archive's bytes.</param>
		/// <returns>The script's bytes, Shift-JIS as NScripter wrote them.</returns>
		public static byte[] Decode(byte[] archive) {
			return Flip(archive);
		}


		/// <summary>
		/// The archive bytes for a script.
		/// </summary>
		/// <param name="script">The script's bytes.</param>
		/// <returns>The archive's bytes.</returns>
		public static byte[] Encode(byte[] script) {
			return Flip(script);
		}


		/// <summary>
		/// Reads an archive file and writes the script beside wherever the caller says. The
		/// archive is only read.
		/// </summary>
		/// <param name="archivePath">The nscript.dat to read.</param>
		/// <param name="scriptPath">The 0.txt to write; its folder is created if needed.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string DecodeFile(string archivePath, string scriptPath) {
			string problem = "";
			if (File.Exists(archivePath) == false) {
				problem = "There is no archive at " + archivePath;
			}
			if (problem.Length == 0) {
				byte[] archive = File.ReadAllBytes(archivePath);
				if (LooksEncoded(archive) == false) {
					problem = archivePath + " is already plain text; there is nothing to decode.";
				}
				if (problem.Length == 0) {
					WriteBytes(scriptPath, Decode(archive));
				}
			}
			return problem;
		}


		/// <summary>
		/// Reads a script file and writes the archive for it. The script is only read.
		/// </summary>
		/// <param name="scriptPath">The 0.txt to read.</param>
		/// <param name="archivePath">The nscript.dat to write; its folder is created if needed.</param>
		/// <returns>Empty on success, otherwise a plain sentence.</returns>
		public static string EncodeFile(string scriptPath, string archivePath) {
			string problem = "";
			if (File.Exists(scriptPath) == false) {
				problem = "There is no script at " + scriptPath;
			}
			if (problem.Length == 0) {
				WriteBytes(archivePath, Encode(File.ReadAllBytes(scriptPath)));
			}
			return problem;
		}


		private static byte[] Flip(byte[] source) {
			byte[] result = new byte[source.Length];
			for (int index = 0; index < source.Length; index++) {
				result[index] = (byte)(source[index] ^ Key);
			}
			return result;
		}


		private static void WriteBytes(string path, byte[] bytes) {
			string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
			if (folder != null && folder.Length > 0) {
				Directory.CreateDirectory(folder);
			}
			File.WriteAllBytes(path, bytes);
		}
	}
}
