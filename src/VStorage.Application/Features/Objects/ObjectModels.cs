using VStorage.Domain.Common;
using VStorage.Domain.Objects;

namespace VStorage.Application.Features.Objects
{
    public sealed record ObjectDto(
        Guid Id,
        Guid BucketId,
        string Key,
        Visibility Visibility,
        Guid OwnerId,
        long Size,
        string Sha256,
        string ContentType,
        string? OriginalFileName,
        DateTimeOffset CreatedAt);

    /// <summary>Metadata plus an open, seekable blob stream (the caller disposes it, the HTTP result does).</summary>
    public sealed record ObjectContent(ObjectDto Object, Stream Content);

    public enum UploadPreconditionKind
    {
        None,
        IfNoneMatchAny,
        IfMatchAny,
        IfMatchETag
    }

    /// <summary>HTTP precondition of an upload (FR-O6). <see cref="ETag"/> is the SHA-256 for IfMatchETag.</summary>
    public sealed record UploadPrecondition(UploadPreconditionKind Kind, string? ETag = null)
    {
        public static readonly UploadPrecondition None = new(UploadPreconditionKind.None);
    }

    /// <param name="Visibility">"shared" or "private"; null = private.</param>
    /// <param name="DeclaredLength">Content-Length when known: lets an oversized upload fail before reading the body.</param>
    public sealed record UploadCommand(
        Guid BucketId,
        string? Key,
        string? Visibility,
        string? ContentType,
        string? FileName,
        UploadPrecondition Precondition,
        long? DeclaredLength,
        Stream Content);

    /// <param name="Created">True for a new key (201); false when an identical object exists or one was overwritten (200).</param>
    public sealed record UploadOutcome(ObjectDto Object, bool Created);

    public sealed record ChangeVisibilityRequest(string? Visibility);

    public static class ObjectAccessErrors
    {
        public static readonly Error InvalidVisibility = Error.Validation("invalid-visibility", "Visibility must be 'shared' or 'private'.");

        public static readonly Error Forbidden = Error.Forbidden("forbidden", "Your role in this bucket does not allow this action on the object.");

        public static readonly Error PreconditionFailed = Error.PreconditionFailed(
            "precondition-failed", "The If-Match / If-None-Match condition is not met for this key.");

        public static Error TooLarge(long maxBytes) => Error.TooLarge("too-large", $"Uploads are limited to {maxBytes} bytes.");
    }

    internal static class ObjectMapping
    {
        public static ObjectDto ToDto(this StoredObject storedObject) => new(
            storedObject.Id,
            storedObject.BucketId,
            storedObject.Key.Value,
            storedObject.Visibility,
            storedObject.OwnerId,
            storedObject.Size ?? 0,
            storedObject.Sha256 ?? "",
            storedObject.ContentType,
            storedObject.OriginalFileName,
            storedObject.CreatedAt);

        /// <summary>"shared" / "private" (any case); null or empty = <paramref name="fallback"/>.</summary>
        public static Result<Visibility?> ParseVisibility(string? value, Visibility? fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Result<Visibility?>.Success(fallback);
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "shared" => Result<Visibility?>.Success(Visibility.Shared),
                "private" => Result<Visibility?>.Success(Visibility.Private),
                _ => ObjectAccessErrors.InvalidVisibility
            };
        }
    }
}
