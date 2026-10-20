using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.DownloadLinks;

namespace VStorage.Infrastructure.Security
{
    /// <summary>
    /// Download tokens (FR-D3, SEC-4): a 42-byte binary payload, encrypted and authenticated by Data Protection, base64url.
    /// The expiry is part of the authenticated payload instead of using ITimeLimitedDataProtector, because the time-limited
    /// protector reports "expired" and "tampered" with the same exception, while FR-D4 needs 410 for one and 404 for the other.
    /// </summary>
    internal sealed class DataProtectionDownloadTokenProtector(IDataProtectionProvider provider) : IDownloadTokenProtector
    {
        // Changing the purpose or the layout invalidates every open link; bump the version byte instead.
        private const string Purpose = "VStorage.DownloadLinks.v1";
        private const byte Version = 1;
        private const int MaxTokenLength = 1024;

        // version (1) | objectId (16) | principalId (16) | inline (1) | expiresAt Unix seconds, big-endian (8)
        private const int PayloadLength = 42;

        private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

        public string Protect(DownloadToken token)
        {
            var payload = new byte[PayloadLength];
            payload[0] = Version;
            token.ObjectId.TryWriteBytes(payload.AsSpan(1, 16));
            token.PrincipalId.TryWriteBytes(payload.AsSpan(17, 16));
            payload[33] = token.Inline ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(34, 8), token.ExpiresAt.ToUnixTimeSeconds());
            return Base64Url.EncodeToString(_protector.Protect(payload));
        }

        public DownloadToken? Unprotect(string token)
        {
            if (token.Length is 0 or > MaxTokenLength)
            {
                return null;
            }

            byte[] payload;
            try
            {
                payload = _protector.Unprotect(Base64Url.DecodeFromChars(token));
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException)
            {
                return null;
            }

            if (payload.Length != PayloadLength || payload[0] != Version || payload[33] > 1)
            {
                return null;
            }

            return new DownloadToken(
                new Guid(payload.AsSpan(1, 16)),
                new Guid(payload.AsSpan(17, 16)),
                payload[33] == 1,
                DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(34, 8))));
        }
    }
}
