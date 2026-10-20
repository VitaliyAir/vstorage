using Microsoft.Extensions.Options;
using VStorage.Application.Options;

namespace VStorage.Api.Http
{
    internal static class ProblemDetailsSetup
    {
        /// <summary>
        /// RFC 9457 problem details for every error. Adds "requestId" (correlation id) and, when the error has a
        /// "code", the problem type "{PublicBaseUrl}/errors/{code}".
        /// </summary>
        public static IServiceCollection AddVStorageProblemDetails(this IServiceCollection services) =>
            services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["requestId"] = context.HttpContext.TraceIdentifier;

                if (context.ProblemDetails.Extensions.TryGetValue(ResultExtensions.CodeExtension, out var code) && code is string slug)
                {
                    var baseUrl = context.HttpContext.RequestServices.GetRequiredService<IOptions<VStorageOptions>>().Value.PublicBaseUrl;
                    context.ProblemDetails.Type = $"{baseUrl.TrimEnd('/')}/errors/{slug}";
                }
            });
    }
}
