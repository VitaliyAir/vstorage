using VStorage.Application.Features.DownloadLinks;

namespace VStorage.Application.Abstractions
{
    /// <summary>Encrypts and authenticates download tokens (FR-D3); implemented with ASP.NET Core Data Protection.</summary>
    public interface IDownloadTokenProtector
    {
        /// <summary>URL-safe text for /d/{token}.</summary>
        string Protect(DownloadToken token);

        /// <summary>The token's content, or null when it is malformed, tampered with or protected with an unknown key.</summary>
        DownloadToken? Unprotect(string token);
    }
}
