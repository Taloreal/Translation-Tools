// File: CheckpointState.cs
// Namespace: TranslationTools
namespace TranslationTools {

	public enum CheckpointEngine { Unknown, NScripter, Siglus }

	public enum CheckpointForm { Unknown, Split, Unsplit, Packed, Invalid }

	/// <summary>
	/// What a checkpoint looks like on disk right now: which engine wrote it and whether it
	/// is split, unsplit, or still a packed archive. Reported to the user, never stored.
	/// </summary>
	public class CheckpointState {

		public CheckpointEngine Engine = CheckpointEngine.Unknown;
		public CheckpointForm Form = CheckpointForm.Unknown;

		/// <summary>Why the checkpoint is invalid, when Form is Invalid; otherwise empty.</summary>
		public string Reason = "";

		/// <summary>A caution about a valid checkpoint, e.g. a master that is missing but rebuildable. Empty when there is none.</summary>
		public string Warning = "";

		/// <summary>True when something rebuildable is missing: the checkpoint must be recovered before any other operation runs on it.</summary>
		public bool NeedsRecovery = false;

		/// <summary>The engine as a word for headers and rows.</summary>
		public string EngineWord {
			get {
				string word = "unknown engine";
				if (Engine == CheckpointEngine.NScripter) {
					word = "NScripter";
				}
				if (Engine == CheckpointEngine.Siglus) {
					word = "Siglus";
				}
				return word;
			}
		}

		/// <summary>The form as a word for headers and rows.</summary>
		public string FormWord {
			get {
				string word = "unknown";
				if (Form == CheckpointForm.Split) {
					word = "split";
				}
				if (Form == CheckpointForm.Unsplit) {
					word = "unsplit";
				}
				if (Form == CheckpointForm.Packed) {
					word = "packed archive";
				}
				if (Form == CheckpointForm.Invalid) {
					word = "invalid";
				}
				return word;
			}
		}


		/// <summary>
		/// Both words together, or "not recognised" when nothing matched.
		/// </summary>
		/// <returns>Text for a row.</returns>
		public string Describe() {
			string text = "not recognised";
			if (Form == CheckpointForm.Invalid) {
				text = "invalid: " + Reason;
			}
			if (Form != CheckpointForm.Invalid && (Engine != CheckpointEngine.Unknown || Form != CheckpointForm.Unknown)) {
				text = EngineWord + " · " + FormWord;
				if (Warning.Length > 0) {
					text += " (warning: " + Warning + ")";
				}
			}
			return text;
		}
	}
}
