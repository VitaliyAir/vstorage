using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VStorage.Api.Http
{
    /// <summary>Tags that select which checks belong to /health/live and /health/ready.</summary>
    internal static class HealthTags
    {
        public const string Live = "live";
        public const string Ready = "ready";
    }

    internal static class HealthResponseWriter
    {
        public static Task WriteAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json";
            var body = new
            {
                status = report.Status.ToString(),
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
                entries = report.Entries.ToDictionary(
                    e => e.Key,
                    e => new { status = e.Value.Status.ToString(), description = e.Value.Description, error = e.Value.Exception?.Message })
            };
            return context.Response.WriteAsync(JsonSerializer.Serialize(body));
        }
    }
}
