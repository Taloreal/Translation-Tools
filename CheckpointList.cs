// File: CheckpointList.cs
// Namespace: TranslationTools
using TALOREAL_NETCORE_API;

namespace TranslationTools {

	/// <summary>
	/// Every checkpoint the tool knows, kept in Settings as one list, plus which one is
	/// selected. Labels are unique, compared without regard to case so two that differ
	/// only by case cannot both exist.
	/// </summary>
	public static class CheckpointList {

		private const string StoreName = "Checkpoints";
		private const string SelectedKey = "Checkpoints.Selected";

		private static readonly AutoListSetting<string> Store = new(StoreName);

		/// <summary>The sentence shown when a label is the reserved Alignment folder's name.</summary>
		public const string ReservedLabelProblem = "\"" + AlignmentRoot.FolderName + "\" is reserved for the alignment folder; pick another label.";

		/// <summary>The label of the selected checkpoint; empty when none is selected.</summary>
		public static string SelectedLabel {
			get {
				Settings.GetValue(SelectedKey, out string? label);
				return label ?? "";
			}
			set { Settings.SetValue(SelectedKey, value); }
		}


		/// <summary>
		/// Every stored checkpoint, in stored order. An entry that cannot be read is skipped.
		/// </summary>
		/// <returns>The checkpoints.</returns>
		public static List<Checkpoint> All() {
			List<Checkpoint> checkpoints = new();
			foreach (string encoded in Store.Value) {
				if (Checkpoint.TryDecode(encoded, out Checkpoint checkpoint) == true) {
					checkpoints.Add(checkpoint);
				}
			}
			if (StampMissingSerials(checkpoints) == true) {
				Save(checkpoints);
			}
			return checkpoints;
		}


		/// <summary>
		/// The checkpoint with a serial, or null.
		/// </summary>
		/// <param name="serial">The serial to look for.</param>
		/// <returns>The checkpoint, or null when no serial matches.</returns>
		public static Checkpoint? FindBySerial(string serial) {
			Checkpoint? found = null;
			foreach (Checkpoint checkpoint in All()) {
				if (found == null && checkpoint.Serial == serial.Trim()) {
					found = checkpoint;
				}
			}
			return found;
		}


		/// <summary>
		/// The checkpoint with a label, or null.
		/// </summary>
		/// <param name="label">The label to look for.</param>
		/// <returns>The checkpoint, or null when no label matches.</returns>
		public static Checkpoint? Find(string label) {
			Checkpoint? found = null;
			foreach (Checkpoint checkpoint in All()) {
				if (found == null && SameLabel(checkpoint.Label, label) == true) {
					found = checkpoint;
				}
			}
			return found;
		}


		/// <summary>
		/// The selected checkpoint. When the stored selection no longer exists, the first
		/// checkpoint is selected instead; null only when there are none at all.
		/// </summary>
		/// <returns>The selected checkpoint, or null.</returns>
		public static Checkpoint? Selected() {
			Checkpoint? selected = Find(SelectedLabel);
			if (selected == null) {
				List<Checkpoint> all = All();
				if (all.Count > 0) {
					selected = all[0];
					SelectedLabel = selected.Label;
				}
			}
			return selected;
		}


		/// <summary>
		/// Moves the selection forward or backward through the list, wrapping at the ends.
		/// </summary>
		/// <param name="step">+1 for the next checkpoint, -1 for the previous.</param>
		public static void CycleSelection(int step) {
			List<Checkpoint> all = All();
			if (all.Count > 0) {
				int current = IndexOf(all, SelectedLabel);
				if (current < 0) {
					current = 0;
				}
				int next = (current + step + all.Count) % all.Count;
				SelectedLabel = all[next].Label;
			}
		}


		/// <summary>
		/// Stores a new checkpoint and selects it, giving it this second as its serial. A
		/// second checkpoint created in the same second is refused, since the serial must be
		/// unique: the caller says to wait a moment and try again.
		/// </summary>
		/// <param name="checkpoint">The checkpoint to add. Its Serial is set here.</param>
		/// <returns>Empty when added; otherwise a plain sentence saying why not.</returns>
		public static string Add(Checkpoint checkpoint) {
			string problem = "";
			bool storable = Checkpoint.IsStorable(checkpoint.Label) == true && Checkpoint.IsStorable(checkpoint.Path) == true;
			if (storable == false) {
				problem = "That label or path cannot be stored.";
			}
			if (problem.Length == 0 && AlignmentRoot.IsReservedLabel(checkpoint.Label) == true) {
				problem = ReservedLabelProblem;
			}
			if (problem.Length == 0 && Find(checkpoint.Label) != null) {
				problem = "A checkpoint is already labelled \"" + checkpoint.Label + "\".";
			}
			if (problem.Length == 0) {
				string serial = Checkpoint.SerialAt(DateTime.Now);
				if (FindBySerial(serial) != null) {
					problem = "A checkpoint was created this very second; wait a moment and try again.";
				}
				if (problem.Length == 0) {
					checkpoint.Serial = serial;
					Store.Add(checkpoint.Encode());
					SelectedLabel = checkpoint.Label;
				}
			}
			return problem;
		}


		/// <summary>
		/// Replaces the stored checkpoint that carries a label with new values. Used for a
		/// rename, a re-point, or a lock change; the old label says which entry to replace.
		/// </summary>
		/// <param name="oldLabel">The label the entry is stored under now.</param>
		/// <param name="updated">The values to store.</param>
		/// <returns>True when replaced; false when no entry had the old label, or the new label is taken by another.</returns>
		public static bool Update(string oldLabel, Checkpoint updated) {
			bool replaced = false;
			List<Checkpoint> all = All();
			int index = IndexOf(all, oldLabel);
			bool storable = Checkpoint.IsStorable(updated.Label) == true && Checkpoint.IsStorable(updated.Path) == true
				&& AlignmentRoot.IsReservedLabel(updated.Label) == false;
			bool labelFree = SameLabel(oldLabel, updated.Label) == true || Find(updated.Label) == null;
			if (index >= 0 && storable == true && labelFree == true) {
				all[index] = updated;
				Save(all);
				if (SameLabel(SelectedLabel, oldLabel) == true) {
					SelectedLabel = updated.Label;
				}
				replaced = true;
			}
			return replaced;
		}


		/// <summary>
		/// Removes the checkpoint with a label. The selection moves to the first remaining
		/// checkpoint when the removed one was selected.
		/// </summary>
		/// <param name="label">The label to remove.</param>
		/// <returns>True when removed; false when no entry had the label.</returns>
		public static bool Remove(string label) {
			bool removed = false;
			List<Checkpoint> all = All();
			int index = IndexOf(all, label);
			if (index >= 0) {
				all.RemoveAt(index);
				Save(all);
				if (SameLabel(SelectedLabel, label) == true) {
					SelectedLabel = "";
					Selected();
				}
				removed = true;
			}
			return removed;
		}


		/// <summary>
		/// Moves the checkpoint with a label one place up (a negative step) or down the list,
		/// wrapping from the top to the bottom and back. Order is the user's grouping; the
		/// tool attaches no meaning to it.
		/// </summary>
		/// <param name="label">The checkpoint to move.</param>
		/// <param name="step">-1 for up, 1 for down.</param>
		/// <returns>True when moved; false when no entry had the label or the list has one entry.</returns>
		public static bool Move(string label, int step) {
			bool moved = false;
			List<Checkpoint> all = All();
			int index = IndexOf(all, label);
			if (index >= 0 && all.Count > 1 && step != 0) {
				Checkpoint moving = all[index];
				all.RemoveAt(index);
				// After the removal the list is one shorter: the last place is all.Count.
				int target = index + step;
				bool wasLast = index == all.Count;
				if (step > 0 && wasLast == true) {
					target = 0;
				}
				if (step < 0 && index == 0) {
					target = all.Count;
				}
				all.Insert(target, moving);
				Save(all);
				moved = true;
			}
			return moved;
		}


		/// <summary>
		/// Whether two labels count as the same: equal ignoring case and surrounding space.
		/// </summary>
		public static bool SameLabel(string first, string second) {
			return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
		}


		/// <summary>
		/// Gives a serial to every checkpoint that has none: entries written before serials
		/// existed. Each gets a different second, counted back from now so none can clash
		/// with a checkpoint created from here on.
		/// </summary>
		/// <param name="all">The checkpoints, stamped in place.</param>
		/// <returns>True when any was stamped, so the caller saves.</returns>
		private static bool StampMissingSerials(List<Checkpoint> all) {
			bool stamped = false;
			DateTime moment = DateTime.Now.AddSeconds(-1);
			foreach (Checkpoint checkpoint in all) {
				if (checkpoint.Serial.Length == 0) {
					string serial = Checkpoint.SerialAt(moment);
					while (HasSerial(all, serial) == true) {
						moment = moment.AddSeconds(-1);
						serial = Checkpoint.SerialAt(moment);
					}
					checkpoint.Serial = serial;
					moment = moment.AddSeconds(-1);
					stamped = true;
				}
			}
			return stamped;
		}


		private static bool HasSerial(List<Checkpoint> all, string serial) {
			bool has = false;
			foreach (Checkpoint checkpoint in all) {
				if (checkpoint.Serial == serial) {
					has = true;
				}
			}
			return has;
		}


		private static int IndexOf(List<Checkpoint> all, string label) {
			int found = -1;
			for (int index = 0; index < all.Count; index++) {
				if (found < 0 && SameLabel(all[index].Label, label) == true) {
					found = index;
				}
			}
			return found;
		}


		private static void Save(List<Checkpoint> all) {
			string[] encoded = new string[all.Count];
			for (int index = 0; index < all.Count; index++) {
				encoded[index] = all[index].Encode();
			}
			Store.Value = encoded;
		}
	}
}
