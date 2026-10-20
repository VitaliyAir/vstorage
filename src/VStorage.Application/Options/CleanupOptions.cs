using System.ComponentModel.DataAnnotations;

namespace VStorage.Application.Options
{
    /// <summary>Retention and background worker settings (section "VStorage:Cleanup").</summary>
    public sealed class CleanupOptions
    {
        public const string SectionName = "VStorage:Cleanup";

        /// <summary>Days a deleted object or bucket is kept before its blobs and rows are purged.</summary>
        [Range(0, 3650)]
        public int SoftDeleteRetentionDays { get; set; } = 30;

        /// <summary>Uploads still pending after this many minutes are treated as abandoned.</summary>
        [Range(1, 10_080)]
        public int PendingTimeoutMinutes { get; set; } = 60;

        [Range(5, 86_400)]
        public int WorkerIntervalSeconds { get; set; } = 60;

        [Range(1, 3600)]
        public int OutboxPollIntervalSeconds { get; set; } = 10;
    }
}
