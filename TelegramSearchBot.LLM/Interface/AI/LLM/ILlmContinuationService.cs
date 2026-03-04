using System.Threading.Tasks;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Interface.AI.LLM {
    /// <summary>
    /// Service for storing and retrieving LLM continuation snapshots.
    /// Used when tool-call iteration limit is reached and user may choose to continue.
    /// </summary>
    public interface ILlmContinuationService {
        /// <summary>
        /// Save a snapshot and return its unique ID.
        /// </summary>
        Task<string> SaveSnapshotAsync(LlmContinuationSnapshot snapshot);

        /// <summary>
        /// Retrieve a snapshot by ID. Returns null if not found or expired.
        /// </summary>
        Task<LlmContinuationSnapshot> GetSnapshotAsync(string snapshotId);

        /// <summary>
        /// Delete a snapshot (after use or on stop).
        /// </summary>
        Task DeleteSnapshotAsync(string snapshotId);

        /// <summary>
        /// Try to acquire an exclusive lock for processing a snapshot.
        /// Returns true if the lock was acquired.
        /// </summary>
        Task<bool> TryAcquireLockAsync(string snapshotId);

        /// <summary>
        /// Release the lock for a snapshot.
        /// </summary>
        Task ReleaseLockAsync(string snapshotId);
    }
}
