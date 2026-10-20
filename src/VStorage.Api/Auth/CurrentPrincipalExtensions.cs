using VStorage.Application.Abstractions;

namespace VStorage.Api.Auth
{
    internal static class CurrentPrincipalExtensions
    {
        private const string ItemKey = "VStorage.CurrentPrincipal";

        public static void SetCurrentPrincipal(this HttpContext context, CurrentPrincipal principal) =>
            context.Items[ItemKey] = principal;

        public static CurrentPrincipal? GetCurrentPrincipal(this HttpContext context) =>
            context.Items.TryGetValue(ItemKey, out var value) ? value as CurrentPrincipal : null;

        /// <summary>For authenticated endpoints: the provisioning middleware always sets the principal there.</summary>
        public static CurrentPrincipal RequireCurrentPrincipal(this HttpContext context) =>
            context.GetCurrentPrincipal()
            ?? throw new InvalidOperationException("No current principal: this endpoint must require an authenticated caller.");
    }
}
