namespace VStorage.Domain.Common
{
    /// <summary>Outcome of an operation without a value: success, or failure with an <see cref="Common.Error"/>.</summary>
    public class Result
    {
        private readonly Error? _error;

        protected Result(bool isSuccess, Error? error)
        {
            if (isSuccess == (error is not null))
            {
                throw new ArgumentException("A success has no error and a failure needs one.", nameof(error));
            }

            IsSuccess = isSuccess;
            _error = error;
        }

        public bool IsSuccess { get; }

        public bool IsFailure => !IsSuccess;

        public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

        public static Result Success() => new(true, null);

        public static Result Failure(Error error) => new(false, error);

        public static implicit operator Result(Error error) => Failure(error);
    }

    /// <summary>Outcome of an operation that returns a value on success.</summary>
    public sealed class Result<T> : Result
    {
        private readonly T? _value;

        private Result(T value)
            : base(true, null)
        {
            _value = value;
        }

        private Result(Error error)
            : base(false, error)
        {
        }

        public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

        public static Result<T> Success(T value) => new(value);

        public static new Result<T> Failure(Error error) => new(error);

        public static implicit operator Result<T>(T value) => Success(value);

        public static implicit operator Result<T>(Error error) => Failure(error);
    }
}
