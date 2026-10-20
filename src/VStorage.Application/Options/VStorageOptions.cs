using System.ComponentModel.DataAnnotations;

namespace VStorage.Application.Options
{
    /// <summary>General settings (section "VStorage").</summary>
    public sealed class VStorageOptions
    {
        public const string SectionName = "VStorage";

        /// <summary>Public origin used to build /d/{token} and /s/{slug} links, e.g. "https://files.example.com".</summary>
        [Required, Url]
        public string PublicBaseUrl { get; set; } = "";
    }
}
