using System.Globalization;
using System.Threading.RateLimiting;

namespace VStorage.Api.Http
{
    /// <summary>Rate limits. S3: user lookup only; stage S10 adds the limits for /s, /d and /api/v1.</summary>
    internal static class RateLimiting
    {
        public const string UserLookupPolicy = "user-lookup";

        private const int UserLookupsPerMinute = 30;

        public static IServiceCollection AddVStorageRateLimiting(this IServiceCollection services) =>
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, _) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    }

                    await TypedResults.Problem(
                            statusCode: StatusCodes.Status429TooManyRequests,
                            title: "Too many requests",
                            detail: "Rate limit exceeded; retry later.",
                            extensions: new Dictionary<string, object?> { [ResultExtensions.CodeExtension] = "rate-limited" })
                        .ExecuteAsync(context.HttpContext);
                };

                // Exact email lookups per principal: prevents enumerating users by guessing addresses.
                options.AddPolicy(UserLookupPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                    http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = UserLookupsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
            });
    }
}
