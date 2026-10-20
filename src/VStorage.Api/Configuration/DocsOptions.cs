namespace VStorage.Api.Configuration
{
    /// <summary>OpenAPI document and Scalar UI (section "VStorage:Docs"); wired up in stage S3.</summary>
    public sealed class DocsOptions
    {
        public const string SectionName = "VStorage:Docs";

        public bool Enabled { get; set; }
    }
}
