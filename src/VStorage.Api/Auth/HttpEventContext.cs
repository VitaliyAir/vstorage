using VStorage.Application.Abstractions;

namespace VStorage.Api.Auth
{
    /// <summary>
    /// Request context written with every outbox event (FR-A6): who (principal), through which client (azp),
    /// delegation chain (act) and the correlation id. No email or other PII.
    /// </summary>
    internal sealed class HttpEventContext(IHttpContextAccessor httpContextAccessor) : IEventContext
    {
        private static readonly IReadOnlyDictionary<string, string?> Empty = new Dictionary<string, string?>();

        public IReadOnlyDictionary<string, string?> Current
        {
            get
            {
                var context = httpContextAccessor.HttpContext;
                if (context is null)
                {
                    return Empty;
                }

                var principal = context.GetCurrentPrincipal();
                return new Dictionary<string, string?>
                {
                    ["principalId"] = principal?.Id.ToString(),
                    ["clientId"] = principal?.ClientId,
                    ["act"] = TokenClaims.Actor(context.User),
                    ["correlationId"] = context.TraceIdentifier
                };
            }
        }
    }
}
