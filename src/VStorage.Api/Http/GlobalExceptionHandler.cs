using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace VStorage.Api.Http
{
    internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
        : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            // The client went away (e.g. aborted upload): nothing to answer, not a server error.
            if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            {
                logger.LogInformation("Request aborted by the client: {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                return true;
            }

            // Kestrel/body errors, e.g. 413 when the request body exceeds the limit.
            if (exception is BadHttpRequestException badRequest)
            {
                logger.LogWarning("Bad request {StatusCode} for {Method} {Path}: {Reason}",
                    badRequest.StatusCode, httpContext.Request.Method, httpContext.Request.Path, badRequest.Message);
                return await WriteAsync(httpContext, exception, badRequest.StatusCode,
                    badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge ? "Request too large" : "Bad request",
                    badRequest.Message);
            }

            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            return await WriteAsync(httpContext, exception, StatusCodes.Status500InternalServerError,
                "Server error", "An unexpected error occurred.");
        }

        private async Task<bool> WriteAsync(HttpContext httpContext, Exception exception, int status, string title, string detail)
        {
            httpContext.Response.StatusCode = status;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
            });
        }
    }
}
