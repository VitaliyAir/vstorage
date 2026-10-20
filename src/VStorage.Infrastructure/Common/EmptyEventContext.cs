using VStorage.Application.Abstractions;

namespace VStorage.Infrastructure.Common
{
    /// <summary>Default event context (background work, tests); the API replaces it in stage S3.</summary>
    internal sealed class EmptyEventContext : IEventContext
    {
        private static readonly IReadOnlyDictionary<string, string?> Empty = new Dictionary<string, string?>();

        public IReadOnlyDictionary<string, string?> Current => Empty;
    }
}
