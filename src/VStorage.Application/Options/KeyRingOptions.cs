using System.ComponentModel.DataAnnotations;

namespace VStorage.Application.Options
{
    /// <summary>ASP.NET Core Data Protection key ring (section "VStorage:DataProtection"): download tokens, later the /s cookie.</summary>
    public sealed class KeyRingOptions
    {
        public const string SectionName = "VStorage:DataProtection";

        /// <summary>
        /// Directory of the key ring XML files. A relative path is resolved against the content root. Keep it outside the
        /// web root and readable only by the service account (SEC-4); losing it invalidates every open download link.
        /// </summary>
        [Required]
        public string KeysPath { get; set; } = "./keys";
    }
}
