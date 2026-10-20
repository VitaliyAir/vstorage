using VStorage.Domain.Common;
using VStorage.Domain.Principals;

namespace VStorage.Application.Features.Principals
{
    /// <summary>Identity claims of a validated token, as needed for provisioning.</summary>
    public sealed record PrincipalClaims(string Subject, PrincipalKind Kind, string? Email, bool EmailVerified, string? DisplayName);

    public sealed record PrincipalDto(Guid Id, PrincipalKind Kind, string? DisplayName, string? Email, bool EmailVerified);

    public sealed record UserLookupDto(Guid Id, string? DisplayName);

    public static class PrincipalErrors
    {
        public static readonly Error InvalidEmail = Error.Validation("invalid-email", "A complete email address is required.");

        public static readonly Error UserNotFound = Error.NotFound("user-not-found", "No user with this verified email.");

        public static readonly Error NotFound = Error.NotFound("not-found", "Principal not found.");
    }
}
