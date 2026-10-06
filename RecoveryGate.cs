// File: RecoveryGate.cs
// Namespace: TranslationTools
namespace TranslationTools {

	/// <summary>
	/// Keeps a checkpoint that needs recovery from being worked on: the moment the inspector
	/// reports a rebuildable piece missing, the checkpoint is locked, and it stays locked -
	/// the user cannot unlock it by hand - until Recover has put the piece back. Recovery
	/// before anything else, enforced by the lock every operation already respects.
	/// </summary>
	public static class RecoveryGate {

		/// <summary>
		/// Locks a checkpoint whose state says it needs recovery. Called wherever a state is
		/// probed, so the lock lands as soon as the condition is seen.
		/// </summary>
		/// <param name="checkpoint">The checkpoint the state belongs to.</param>
		/// <param name="state">Its state as just probed.</param>
		/// <returns>True when this call locked it.</returns>
		public static bool Enforce(Checkpoint checkpoint, CheckpointState state) {
			bool locked = false;
			if (state.NeedsRecovery == true && checkpoint.Writable == true) {
				Checkpoint updated = checkpoint;
				updated.Writable = false;
				CheckpointList.Update(checkpoint.Label, updated);
				CheckpointLog.Warning(CheckpointInspector.FolderOf(checkpoint.Path), "Recovery",
					"locked until recovered: " + state.Warning);
				locked = true;
			}
			return locked;
		}


		/// <summary>
		/// Whether a checkpoint may be unlocked by hand right now. Not while it needs recovery.
		/// </summary>
		/// <param name="checkpoint">The checkpoint.</param>
		/// <param name="reason">Why not, when the answer is no.</param>
		/// <returns>True when the user may unlock it.</returns>
		public static bool MayUnlock(Checkpoint checkpoint, out string reason) {
			reason = "";
			CheckpointState state = CheckpointInspector.Inspect(checkpoint.Path);
			if (state.NeedsRecovery == true) {
				reason = "\"" + checkpoint.Label + "\" needs recovery first (" + state.Warning + "). Run Recover from the operations menu.";
			}
			return reason.Length == 0;
		}
	}
}
