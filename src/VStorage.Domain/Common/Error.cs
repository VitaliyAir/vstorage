namespace VStorage.Domain.Common
{
    /// <summary>Category of a failure; the API maps it to an HTTP status code.</summary>
    public enum ErrorType
    {
        Validation,
        Unauthorized,
        Forbidden,
        NotFound,
        Conflict,
        Gone,
        PreconditionFailed,
        TooLarge
    }

    /// <summary>
    /// A business failure. <see cref="Code"/> is the problem-type slug (e.g. "not-found", "key-exists"),
    /// <see cref="Message"/> the human-readable detail.
    /// </summary>
    public sealed record Error(string Code, string Message, ErrorType Type)
    {
        public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

        public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

        public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

        public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

        public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

        public static Error Gone(string code, string message) => new(code, message, ErrorType.Gone);

        public static Error PreconditionFailed(string code, string message) => new(code, message, ErrorType.PreconditionFailed);

        public static Error TooLarge(string code, string message) => new(code, message, ErrorType.TooLarge);
    }
}
