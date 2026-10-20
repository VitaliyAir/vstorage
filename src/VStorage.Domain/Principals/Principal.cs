using VStorage.Domain.Common;

namespace VStorage.Domain.Principals
{
    public enum PrincipalKind
    {
        User,
        Service
    }

    /// <summary>
    /// A VStorage user record (table "users"): a person or a Keycloak service account, identified only by "sub".
    /// Email and display name are copied from the token for display and lookup, never for authorization.
    /// </summary>
    public sealed class Principal : Entity
    {
        private Principal()
        {
        }

        private Principal(Guid id, string keycloakSub, PrincipalKind kind, DateTimeOffset now)
            : base(id)
        {
            KeycloakSub = keycloakSub;
            Kind = kind;
            CreatedAt = now;
            LastSeenAt = now;
        }

        public string KeycloakSub { get; private set; } = "";

        public PrincipalKind Kind { get; private set; }

        /// <summary>Lower-cased email from the token, or null.</summary>
        public string? Email { get; private set; }

        /// <summary>True only when an email is present and the token says it is verified.</summary>
        public bool EmailVerified { get; private set; }

        public string? DisplayName { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        public DateTimeOffset LastSeenAt { get; private set; }

        public static Principal Create(
            string keycloakSub, PrincipalKind kind, string? email, bool emailVerified, string? displayName, DateTimeOffset now)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keycloakSub);

            var principal = new Principal(Guid.CreateVersion7(now), keycloakSub, kind, now);
            principal.UpdateProfile(email, emailVerified, displayName);
            return principal;
        }

        /// <summary>Copies profile claims; returns true when something changed (so callers only write on change).</summary>
        public bool UpdateProfile(string? email, bool emailVerified, string? displayName)
        {
            var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
            var verified = normalizedEmail is not null && emailVerified;
            var name = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

            if (normalizedEmail == Email && verified == EmailVerified && name == DisplayName)
            {
                return false;
            }

            Email = normalizedEmail;
            EmailVerified = verified;
            DisplayName = name;
            return true;
        }

        /// <summary>Updates LastSeenAt at most once per <paramref name="minInterval"/>; returns true when updated.</summary>
        public bool Touch(DateTimeOffset now, TimeSpan minInterval)
        {
            if (now - LastSeenAt < minInterval)
            {
                return false;
            }

            LastSeenAt = now;
            return true;
        }
    }
}
