using VStorage.Domain.Common;

namespace VStorage.Domain.Objects
{
    public static class ObjectErrors
    {
        public static readonly Error NotFound = Error.NotFound("not-found", "Object not found.");

        public static readonly Error InvalidContentType = Error.Validation("invalid-content-type", "Content type must look like 'type/subtype' (max 255 characters).");

        public static readonly Error InvalidFileName = Error.Validation("invalid-file-name", "File name must be at most 255 characters without control characters.");

        public static readonly Error InvalidContent = Error.Validation("invalid-content", "Size must not be negative and SHA-256 must be 64 lower-case hex characters.");

        public static readonly Error NotPending = Error.Conflict("not-pending", "The upload of this object is already finished.");

        public static readonly Error NotAvailable = Error.NotFound("not-found", "Object not found.");

        public static readonly Error AlreadyShared = Error.Conflict("already-shared", "The object is already shared.");

        public static readonly Error SharedToPrivateForbidden = Error.Conflict("shared-to-private-forbidden", "A shared object cannot be made private.");

        public static readonly Error KeyExists = Error.Conflict("key-exists", "An object with this key already exists.");

        public static Error InvalidKey(string detail) => Error.Validation("invalid-key", detail);
    }
}
