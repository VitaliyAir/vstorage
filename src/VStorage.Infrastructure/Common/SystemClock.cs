using VStorage.Application.Abstractions;

namespace VStorage.Infrastructure.Common
{
    internal sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
