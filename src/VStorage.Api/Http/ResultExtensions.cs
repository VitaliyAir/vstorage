using VStorage.Domain.Common;

namespace VStorage.Api.Http
{
    /// <summary>Maps <see cref="Result"/> to HTTP: success → 200/204, failure → problem details (spec §9.6).</summary>
    internal static class ResultExtensions
    {
        public const string CodeExtension = "code";

        public static IResult ToHttp(this Result result) =>
            result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();

        public static IResult ToHttp<T>(this Result<T> result) =>
            result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();

        public static IResult ToProblem(this Error error)
        {
            var (status, title) = error.Type switch
            {
                ErrorType.Validation => (StatusCodes.Status400BadRequest, "Validation failed"),
                ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
                ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
                ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not found"),
                ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
                ErrorType.Gone => (StatusCodes.Status410Gone, "Gone"),
                ErrorType.PreconditionFailed => (StatusCodes.Status412PreconditionFailed, "Precondition failed"),
                ErrorType.TooLarge => (StatusCodes.Status413PayloadTooLarge, "Too large"),
                _ => (StatusCodes.Status500InternalServerError, "Server error")
            };

            return TypedResults.Problem(
                statusCode: status,
                title: title,
                detail: error.Message,
                extensions: new Dictionary<string, object?> { [CodeExtension] = error.Code });
        }
    }
}
