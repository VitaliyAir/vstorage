using VStorage.Domain.Principals;

namespace VStorage.Application.Abstractions
{
    /// <summary>
    /// The caller of the current request after provisioning: the VStorage principal id plus what the token says
    /// (calling client "azp", platform roles). Bucket roles are NOT here; they always come from the database.
    /// </summary>
    public sealed record CurrentPrincipal(
        Guid Id,
        PrincipalKind Kind,
        string? ClientId,
        bool IsPlatformAdmin,
        bool CanCreateBuckets);
}
