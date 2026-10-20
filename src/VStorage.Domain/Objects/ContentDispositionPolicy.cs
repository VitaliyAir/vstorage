namespace VStorage.Domain.Objects
{
    /// <summary>
    /// Stored-XSS protection (spec SEC-6): a file is shown inline only for an allow-list of passive types
    /// (images except SVG, PDF, audio, video, plain text). Everything else - HTML, SVG, XML, scripts - is an attachment.
    /// </summary>
    public static class ContentDispositionPolicy
    {
        public static bool IsInlineAllowed(string contentType)
        {
            var mediaType = contentType.Split(';')[0].Trim().ToLowerInvariant();
            if (mediaType == "image/svg+xml")
            {
                return false;
            }

            return mediaType.StartsWith("image/", StringComparison.Ordinal)
                || mediaType.StartsWith("audio/", StringComparison.Ordinal)
                || mediaType.StartsWith("video/", StringComparison.Ordinal)
                || mediaType is "application/pdf" or "text/plain";
        }
    }
}
