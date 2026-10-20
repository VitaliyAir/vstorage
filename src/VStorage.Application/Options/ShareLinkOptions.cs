using System.ComponentModel.DataAnnotations;

namespace VStorage.Application.Options
{
    /// <summary>Share link settings (section "VStorage:ShareLinks").</summary>
    public sealed class ShareLinkOptions : IValidatableObject
    {
        public const string SectionName = "VStorage:ShareLinks";

        [Range(typeof(TimeSpan), "00:01:00", "3650.00:00:00")]
        public TimeSpan DefaultExpiresIn { get; set; } = TimeSpan.FromDays(30);

        [Range(typeof(TimeSpan), "00:01:00", "3650.00:00:00")]
        public TimeSpan MaxExpiresIn { get; set; } = TimeSpan.FromDays(365);

        [Range(8, 32)]
        public int PublicSlugLength { get; set; } = 12;

        [Range(8, 32)]
        public int AddressedSlugLength { get; set; } = 8;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (DefaultExpiresIn > MaxExpiresIn)
            {
                yield return new ValidationResult(
                    "DefaultExpiresIn must not exceed MaxExpiresIn.",
                    [nameof(DefaultExpiresIn), nameof(MaxExpiresIn)]);
            }
        }
    }
}
