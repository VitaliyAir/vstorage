using System.ComponentModel.DataAnnotations;

namespace VStorage.Application.Options
{
    /// <summary>Blob storage settings (section "VStorage:Storage").</summary>
    public sealed class StorageOptions
    {
        public const string SectionName = "VStorage:Storage";

        /// <summary>Root directory of tmp/ and objects/. A relative path is resolved against the content root.</summary>
        [Required]
        public string BlobRoot { get; set; } = "./data";

        /// <summary>Largest accepted upload in bytes (default 1 GB).</summary>
        [Range(1L, long.MaxValue)]
        public long MaxUploadBytes { get; set; } = 1L << 30;

        /// <summary>Readiness fails when free disk space under <see cref="BlobRoot"/> drops below this (default 5 GB).</summary>
        [Range(0L, long.MaxValue)]
        public long MinFreeDiskBytes { get; set; } = 5L << 30;
    }
}
