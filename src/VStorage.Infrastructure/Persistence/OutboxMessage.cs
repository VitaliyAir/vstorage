namespace VStorage.Infrastructure.Persistence
{
    /// <summary>A domain event waiting for the outbox dispatcher (table outbox_messages).</summary>
    public sealed class OutboxMessage
    {
        public long Id { get; set; }

        /// <summary>Event type name, e.g. "ObjectCreated".</summary>
        public required string Type { get; init; }

        /// <summary>JSON: { "data": { event fields }, "context": { request context } }.</summary>
        public required string Payload { get; init; }

        public DateTimeOffset OccurredAt { get; init; }

        public DateTimeOffset? ProcessedAt { get; set; }

        public int Attempts { get; set; }

        public string? LastError { get; set; }
    }
}
