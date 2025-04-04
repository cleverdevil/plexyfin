using System;

namespace Jellyfin.Plugin.Plexyfin.Api
{
    /// <summary>
    /// Status class to track sync progress.
    /// </summary>
    public class SyncStatus
    {
        /// <summary>
        /// Gets or sets the unique ID for this sync operation.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Gets or sets the overall progress (0-100).
        /// </summary>
        public int Progress { get; set; }
        
        /// <summary>
        /// Gets or sets the current status message.
        /// </summary>
        public string Message { get; set; } = "Initializing...";
        
        /// <summary>
        /// Gets or sets a value indicating whether the operation is complete.
        /// </summary>
        public bool IsComplete { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether this is a dry run.
        /// </summary>
        public bool IsDryRun { get; set; }
        
        /// <summary>
        /// Gets or sets the results of the sync operation.
        /// </summary>
        public SyncResult? Result { get; set; }
        
        /// <summary>
        /// Gets or sets the time when the sync was started.
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Gets or sets the time when the sync was completed.
        /// </summary>
        public DateTime? EndTime { get; set; }
        
        /// <summary>
        /// Gets the elapsed time since the start of the sync.
        /// </summary>
        public TimeSpan ElapsedTime => (EndTime ?? DateTime.UtcNow) - StartTime;
        
        /// <summary>
        /// Gets or sets the total number of items to process.
        /// </summary>
        public int TotalItems { get; set; }
        
        /// <summary>
        /// Gets or sets the number of items remaining to process.
        /// </summary>
        public int RemainingItems { get; set; }
        
        /// <summary>
        /// Gets or sets the number of items processed so far.
        /// </summary>
        public int ProcessedItems { get; set; }
    }
}