using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VStorage.Infrastructure.Persistence.Configurations
{
    /// <summary>Stores enums as lower-case text ('shared', 'available', ...), matching the CHECK constraints.</summary>
    internal static class EnumText
    {
        public static ValueConverter<TEnum, string> Converter<TEnum>()
            where TEnum : struct, Enum =>
            new(value => value.ToString().ToLowerInvariant(), text => Enum.Parse<TEnum>(text, true));
    }
}
