using System.ComponentModel.DataAnnotations;

namespace VStorage.Application.Options
{
    /// <summary>Short-lived download link settings (section "VStorage:DownloadLinks").</summary>
    public sealed class DownloadLinkOptions : IValidatableObject
    {
        public const string SectionName = "VStorage:DownloadLinks";

        [Range(10, 86_400)]
        public int DefaultLifetimeSeconds { get; set; } = 300;

        [Range(10, 86_400)]
        public int MaxLifetimeSeconds { get; set; } = 3600;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (DefaultLifetimeSeconds > MaxLifetimeSeconds)
            {
                yield return new ValidationResult(
                    "DefaultLifetimeSeconds must not exceed MaxLifetimeSeconds.",
                    [nameof(DefaultLifetimeSeconds), nameof(MaxLifetimeSeconds)]);
            }
        }
    }
}
