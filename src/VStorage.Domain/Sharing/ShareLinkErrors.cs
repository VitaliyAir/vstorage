using VStorage.Domain.Common;

namespace VStorage.Domain.Sharing
{
    public static class ShareLinkErrors
    {
        public static readonly Error InvalidSlug = Error.Validation("invalid-slug", "Slug must be 8-32 base62 characters.");

        public static readonly Error ExpiryInPast = Error.Validation("invalid-expiry", "expiresAt must be in the future.");

        public static readonly Error InvalidMaxDownloads = Error.Validation("invalid-max-downloads", "maxDownloads must be empty or greater than zero.");

        public static readonly Error AlreadyRevoked = Error.Conflict("already-revoked", "The share link is already revoked.");

        public static readonly Error NotFound = Error.NotFound("not-found", "Share link not found.");

        public static readonly Error Expired = Error.Gone("expired", "The share link has expired.");

        /// <summary>maxDownloads is reached (FR-S5).</summary>
        public static readonly Error Exhausted = Error.Gone("exhausted", "The share link has reached its download limit.");

        /// <summary>An addressed link opened without a session: the endpoint answers with the Keycloak sign-in (FR-A10).</summary>
        public static readonly Error SignInRequired = Error.Unauthorized("sign-in-required", "Sign in to open this share link.");

        public static readonly Error UnknownRecipient = Error.Validation("unknown-recipient", "Every recipient must be an existing user.");

        public static Error TooManyRecipients(int max) => Error.Validation("too-many-recipients", $"A share link has at most {max} recipients.");

        public static Error ExpiryTooLate(DateTimeOffset latest) => Error.Validation("invalid-expiry", $"expiresAt must not be later than {latest:O}.");
    }
}
