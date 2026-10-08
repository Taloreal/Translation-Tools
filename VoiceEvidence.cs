// File: VoiceEvidence.cs
// Namespace: TranslationTools
using System.Text;

namespace TranslationTools {

	/// <summary>
	/// What the audio says about two dialogue lines: the same recorded clip, different
	/// clips, or nothing. A clip is named by its digits. Siglus names it in koe(id, channel)
	/// just before the line, an engine fact that holds for every Siglus game. NScripter has
	/// too many ways to play audio to read generally, so an NScripter line gets voice
	/// evidence only by probe: when the other side is Siglus, its digits are looked for in
	/// the code between the previous dialogue token and this one. Under the TGD switch the
	/// patch's own shape, mov $voiceN,"Z#.wav":gosub *voiceplay, gives NScripter lines keys
	/// of their own.
	/// </summary>
	public class VoiceEvidence {

		/// <summary>The verdict when both lines carry the same clip.</summary>
		public const string SameClip = "same";

		/// <summary>The verdict when the audio rules the pair out: different clips, or one voiced and one not.</summary>
		public const string Different = "different";

		/// <summary>The verdict when the audio says nothing either way.</summary>
		public const string None = "none";

		/// <summary>The fewest digits a clip number must have for the probe to trust it.</summary>
		public const int LeastDigits = 6;

		private readonly Dictionary<int, string> editKeys;
		private readonly Dictionary<int, string> refKeys;
		private readonly Dictionary<int, string> editCodeBefore;
		private readonly Dictionary<int, string> refCodeBefore;
		private readonly bool editHasKeys;
		private readonly bool refHasKeys;


		/// <summary>
		/// Reads both sides' voice facts for one dialogue key.
		/// </summary>
		/// <param name="edit">The editable checkpoint.</param>
		/// <param name="reference">The reference checkpoint.</param>
		/// <param name="editKey">The editable side's dialogue key.</param>
		/// <param name="refKey">The reference side's dialogue key.</param>
		public VoiceEvidence(Checkpoint edit, Checkpoint reference, string editKey, string refKey) {
			editKeys = KeysFor(edit, editKey, out editHasKeys, out editCodeBefore);
			refKeys = KeysFor(reference, refKey, out refHasKeys, out refCodeBefore);
		}


		/// <summary>
		/// The verdict for two lines, and a sentence saying why, for the screen.
		/// </summary>
		/// <param name="editIndex">The editable line's index.</param>
		/// <param name="refIndex">The reference line's index.</param>
		/// <param name="note">What the audio showed, in words; empty when nothing.</param>
		/// <returns>SameClip, Different or None.</returns>
		public string Verdict(int editIndex, int refIndex, out string note) {
			string verdict = None;
			note = "";
			string editKey = KeyOf(editKeys, editIndex);
			string refKey = KeyOf(refKeys, refIndex);
			if (editKey.Length > 0 && refKey.Length > 0) {
				verdict = Different;
				note = "Audio: different clips, " + editKey + " against " + refKey + ".";
				if (editKey == refKey) {
					verdict = SameClip;
					note = "Audio: the same clip, " + editKey + ".";
				}
			}
			if (verdict == None && editHasKeys == true && refHasKeys == true && editKey.Length != refKey.Length) {
				// Both sides name their clips, and only one of these two lines does: a voiced
				// line against an unvoiced one.
				verdict = Different;
				note = "Audio: one line is voiced, the other is not.";
			}
			if (verdict == None && editKey.Length > 0 && refHasKeys == false && refCodeBefore.ContainsKey(refIndex) == true) {
				if (CodeMentions(refCodeBefore[refIndex], editKey) == true) {
					verdict = SameClip;
					note = "Audio: clip " + editKey + " is named in the code before the other line.";
				}
			}
			if (verdict == None && refKey.Length > 0 && editHasKeys == false && editCodeBefore.ContainsKey(editIndex) == true) {
				if (CodeMentions(editCodeBefore[editIndex], refKey) == true) {
					verdict = SameClip;
					note = "Audio: clip " + refKey + " is named in the code before the other line.";
				}
			}
			return verdict;
		}


		/// <summary>
		/// Whether a stretch of code names a clip number: the digit run appears, and the
		/// run is long enough to be a clip rather than a wait or a coordinate.
		/// </summary>
		public static bool CodeMentions(string code, string digits) {
			bool mentions = false;
			if (digits.Length >= LeastDigits) {
				int at = code.IndexOf(digits, StringComparison.Ordinal);
				while (mentions == false && at >= 0) {
					bool digitBefore = at > 0 && char.IsAsciiDigit(code[at - 1]);
					bool digitAfter = at + digits.Length < code.Length && char.IsAsciiDigit(code[at + digits.Length]);
					if (digitBefore == false && digitAfter == false) {
						mentions = true;
					}
					at = code.IndexOf(digits, at + 1, StringComparison.Ordinal);
				}
			}
			return mentions;
		}


		/// <summary>
		/// Siglus: the koe digits bound to each dialogue index of one key, read from every
		/// scene file in the split. Only the regions after that key's pointer comment count,
		/// since ids restart in every region, and a cue dies at the next token.
		/// </summary>
		public static Dictionary<int, string> SiglusKeys(string splitFolder, string key) {
			Dictionary<int, string> keys = new();
			if (Directory.Exists(splitFolder) == true) {
				foreach (string scenePath in Directory.GetFiles(splitFolder, "*.ss")) {
					string text = SiglusScript.ReadScript(scenePath);
					bool inRegion = false;
					string pending = "";
					foreach (ScriptLine line in SiglusScript.ReadLines(text)) {
						string pointed = SiglusScript.PointerTarget(line.Content);
						bool isLabel = SiglusScript.LabelOf(line.Content).Length > 0;
						if (pointed.Length > 0 || isLabel == true) {
							inRegion = pointed.Equals(key, StringComparison.OrdinalIgnoreCase);
							pending = "";
						}
						if (inRegion == true && pointed.Length == 0 && isLabel == false) {
							string cue = KoeDigits(line.Content);
							if (cue.Length > 0) {
								pending = cue;
							}
							if (cue.Length == 0 && SiglusScript.TryReadToken(line.Content, out int id) == true) {
								if (pending.Length > 0) {
									keys[id] = pending;
								}
								pending = "";
							}
						}
					}
				}
			}
			return keys;
		}


		/// <summary>
		/// NScripter: for each dialogue index of a key, the function file's lines between
		/// the previous pointer and this one, joined, for the probe to search.
		/// </summary>
		public static Dictionary<int, string> NScripterCodeBefore(string splitFolder, string key) {
			Dictionary<int, string> before = new();
			string path = Path.Combine(splitFolder, NScripterSplit.FunctionsFolder, key + ".txt");
			if (File.Exists(path) == true) {
				StringBuilder stretch = new();
				foreach (string line in NScripterSplit.ReadLines(path)) {
					if (NScripterSplit.TryReadPointer(line, out string pointer, out int index, out string rest) == true) {
						before[index] = stretch.ToString();
						stretch.Clear();
					}
					if (NScripterSplit.TryReadPointer(line, out string ignoredPointer, out int ignoredIndex, out string ignoredRest) == false) {
						stretch.Append(line).Append('\n');
					}
				}
			}
			return before;
		}


		/// <summary>
		/// NScripter under the TGD switch: the digits of the clip in mov $voiceN,"Z#.wav"
		/// bound to the next pointer.
		/// </summary>
		public static Dictionary<int, string> NScripterTgdKeys(string splitFolder, string key) {
			Dictionary<int, string> keys = new();
			string path = Path.Combine(splitFolder, NScripterSplit.FunctionsFolder, key + ".txt");
			if (File.Exists(path) == true) {
				string pending = "";
				foreach (string line in NScripterSplit.ReadLines(path)) {
					string cue = VoiceplayDigits(line);
					if (cue.Length > 0) {
						pending = cue;
					}
					if (cue.Length == 0 && NScripterSplit.TryReadPointer(line, out string pointer, out int index, out string rest) == true) {
						if (pending.Length > 0) {
							keys[index] = pending;
						}
						pending = "";
					}
				}
			}
			return keys;
		}


		/// <summary>
		/// The digits of the first argument of a koe(...) call at the start of a line, or empty.
		/// </summary>
		public static string KoeDigits(string line) {
			string digits = "";
			string trimmed = line.Trim();
			if (trimmed.StartsWith("koe(", StringComparison.OrdinalIgnoreCase) == true) {
				int close = trimmed.IndexOf(')');
				if (close > 4) {
					string inside = trimmed.Substring(4, close - 4);
					string first = inside.Split(',')[0];
					digits = Digits(first);
				}
			}
			return digits;
		}


		/// <summary>
		/// The digits of the clip file in a TGD voiceplay line, or empty.
		/// </summary>
		public static string VoiceplayDigits(string line) {
			string digits = "";
			string bare = line.Replace(" ", "").Replace("\t", "");
			if (bare.Contains(":gosub*voiceplay", StringComparison.OrdinalIgnoreCase) == true) {
				int open = line.IndexOf('"');
				int close = line.LastIndexOf('"');
				if (open >= 0 && close > open) {
					digits = Digits(line.Substring(open + 1, close - open - 1));
				}
			}
			return digits;
		}


		/// <summary>
		/// The digits of a clip name, leading zeros kept, so "Z001100231.wav" and
		/// "001100231" are the same key.
		/// </summary>
		private static string Digits(string text) {
			StringBuilder digits = new();
			foreach (char letter in text) {
				if (char.IsAsciiDigit(letter) == true) {
					digits.Append(letter);
				}
			}
			return digits.ToString();
		}


		private static Dictionary<int, string> KeysFor(Checkpoint checkpoint, string key, out bool hasKeys, out Dictionary<int, string> codeBefore) {
			Dictionary<int, string> keys = new();
			codeBefore = new Dictionary<int, string>();
			hasKeys = false;
			string split = Path.Combine(CheckpointInspector.FolderOf(checkpoint.Path), CheckpointInspector.SplitFolder);
			CheckpointState state = CheckpointInspector.Inspect(checkpoint.Path);
			if (state.Engine == CheckpointEngine.Siglus) {
				keys = SiglusKeys(split, key);
				hasKeys = true;
			}
			if (state.Engine == CheckpointEngine.NScripter) {
				codeBefore = NScripterCodeBefore(split, key);
				if (TgdFeatures.Enabled == true) {
					keys = NScripterTgdKeys(split, key);
					hasKeys = true;
				}
			}
			return keys;
		}


		private static string KeyOf(Dictionary<int, string> keys, int index) {
			string key = "";
			if (keys.TryGetValue(index, out string? found) == true && found != null) {
				key = found;
			}
			return key;
		}
	}
}
