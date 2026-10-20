namespace VStorage.Application.Abstractions
{
    /// <summary>
    /// Request context stored with every outbox event under "context" (spec FR-A6).
    /// Empty until stage S3, which adds the principal id, the calling client (azp), "act" and the correlation id.
    /// </summary>
    public interface IEventContext
    {
        IReadOnlyDictionary<string, string?> Current { get; }
    }
}
