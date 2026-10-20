namespace VStorage.Application.Abstractions
{
    /// <summary>Current UTC time (replaceable in tests).</summary>
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }
}
