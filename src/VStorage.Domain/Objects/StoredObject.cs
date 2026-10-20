using System.Text.RegularExpressions;
using VStorage.Domain.Common;
using VStorage.Domain.Events;

namespace VStorage.Domain.Objects
{
    public enum Visibility
    {
        Shared,
        Private
    }

    /// <summary>Pending (bytes being written) → Available → Deleted (purged after retention).</summary>
    public enum ObjectStatus
    {
        Pending,
        Available,
        Deleted
    }

    /// <summary>
    /// A stored file (table "objects"). Immutable content: an overwrite creates a new object.
    /// Its blob lives at objects/{bucketId}/{id[^2..]}/{id[^4..^2]}/{id}.
    /// </summary>
    public sealed partial class StoredObject : Entity
    {
        public const string DefaultContentType = "application/octet-stream";
        private const int MaxTextLength = 255;

        private StoredObject()
        {
        }

        private StoredObject(
            Guid id, Guid bucketId, ObjectKey key, Guid ownerId, Visibility visibility, string contentType, string? originalFileName, DateTimeOffset now)
            : base(id)
        {
            BucketId = bucketId;
            Key = key;
            OwnerId = ownerId;
            Visibility = visibility;
            Status = ObjectStatus.Pending;
            ContentType = contentType;
            OriginalFileName = originalFileName;
            CreatedAt = now;
            UpdatedAt = now;
        }

        public Guid BucketId { get; private set; }

        public ObjectKey Key { get; private set; } = null!;

        public Guid OwnerId { get; private set; }

        public Visibility Visibility { get; private set; }

        public ObjectStatus Status { get; private set; }

        public long? Size { get; private set; }

        public string? Sha256 { get; private set; }

        public string ContentType { get; private set; } = DefaultContentType;

        public string? OriginalFileName { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset UpdatedAt { get; private set; }

        public DateTimeOffset? DeletedAt { get; private set; }

        /// <summary>Starts an upload: the row exists (status pending) before any byte is written.</summary>
        public static Result<StoredObject> BeginUpload(
            Guid bucketId, ObjectKey key, Guid ownerId, Visibility visibility, string? contentType, string? originalFileName, DateTimeOffset now)
        {
            if (contentType is not null && contentType.Any(char.IsControl))
            {
                return ObjectErrors.InvalidContentType;
            }

            var type = string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType.Trim();
            if (type.Length > MaxTextLength || !ContentTypePattern().IsMatch(type))
            {
                return ObjectErrors.InvalidContentType;
            }

            var fileName = NormalizeFileName(originalFileName);
            if (fileName is { Length: > MaxTextLength } || (fileName?.Any(char.IsControl) ?? false))
            {
                return ObjectErrors.InvalidFileName;
            }

            return new StoredObject(Guid.CreateVersion7(now), bucketId, key, ownerId, visibility, type, fileName, now);
        }

        /// <summary>The blob is on disk: the object becomes visible.</summary>
        public Result Complete(long size, string sha256, DateTimeOffset now)
        {
            if (Status != ObjectStatus.Pending)
            {
                return ObjectErrors.NotPending;
            }

            if (size < 0 || !Sha256Pattern().IsMatch(sha256))
            {
                return ObjectErrors.InvalidContent;
            }

            Size = size;
            Sha256 = sha256;
            Status = ObjectStatus.Available;
            UpdatedAt = now;
            Raise(new ObjectCreated(Id, BucketId, Key.Value, OwnerId, Visibility, size, sha256));
            return Result.Success();
        }

        /// <summary>Soft delete of an available object (delete, overwrite, member removal).</summary>
        public Result Delete(DateTimeOffset now)
        {
            if (Status != ObjectStatus.Available)
            {
                return ObjectErrors.NotAvailable;
            }

            Status = ObjectStatus.Deleted;
            DeletedAt = now;
            UpdatedAt = now;
            Raise(new ObjectDeleted(Id, BucketId, OwnerId));
            return Result.Success();
        }

        /// <summary>A failed or abandoned upload: marked deleted without an event (it was never visible).</summary>
        public void Abandon(DateTimeOffset now)
        {
            if (Status != ObjectStatus.Pending)
            {
                return;
            }

            Status = ObjectStatus.Deleted;
            DeletedAt = now;
            UpdatedAt = now;
        }

        /// <summary>private → shared. The caller checks that the shared key is free (unique index is the final guard).</summary>
        public Result MakeShared(DateTimeOffset now)
        {
            if (Status != ObjectStatus.Available)
            {
                return ObjectErrors.NotAvailable;
            }

            if (Visibility == Visibility.Shared)
            {
                return ObjectErrors.AlreadyShared;
            }

            Visibility = Visibility.Shared;
            UpdatedAt = now;
            Raise(new ObjectVisibilityChanged(Id, BucketId, Visibility));
            return Result.Success();
        }

        /// <summary>Visibility rule only (shared, or own private); bucket membership is checked separately.</summary>
        public bool IsVisibleTo(Guid principalId) =>
            Status == ObjectStatus.Available && (Visibility == Visibility.Shared || OwnerId == principalId);

        // Keeps only the last path component of a client-supplied file name ("C:\docs\a.pdf" -> "a.pdf").
        private static string? NormalizeFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var name = fileName.Trim();
            var lastSeparator = name.LastIndexOfAny(['/', '\\']);
            name = lastSeparator >= 0 ? name[(lastSeparator + 1)..] : name;
            return name.Length == 0 ? null : name;
        }

        [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]*/[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]*(\s*;.*)?$")]
        private static partial Regex ContentTypePattern();

        [GeneratedRegex("^[0-9a-f]{64}$")]
        private static partial Regex Sha256Pattern();
    }
}
