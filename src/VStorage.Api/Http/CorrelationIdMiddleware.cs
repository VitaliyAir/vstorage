using System.Text.RegularExpressions;
using Serilog.Context;

namespace VStorage.Api.Http
{
    /// <summary>
    /// Takes the correlation id from X-Request-Id (or generates one), uses it as the request's TraceIdentifier,
    /// returns it in the response and adds it to every log entry of the request.
    /// </summary>
    internal sealed partial class CorrelationIdMiddleware(RequestDelegate next)
    {
        public const string HeaderName = "X-Request-Id";

        public async Task InvokeAsync(HttpContext context)
        {
            var incoming = context.Request.Headers[HeaderName].ToString();
            var correlationId = IsValid(incoming) ? incoming : Guid.NewGuid().ToString("N");

            context.TraceIdentifier = correlationId;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = correlationId;
                return Task.CompletedTask;
            });

            using (LogContext.PushProperty("CorrelationId", correlationId))
            {
                await next(context);
            }
        }

        // Only short, log-safe ids are accepted from callers; anything else is replaced.
        private static bool IsValid(string value) => value.Length is > 0 and <= 64 && AllowedCharacters().IsMatch(value);

        [GeneratedRegex("^[A-Za-z0-9._-]+$")]
        private static partial Regex AllowedCharacters();
    }
}
