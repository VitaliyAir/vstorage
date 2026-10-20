using System.Text.RegularExpressions;
using VStorage.Domain.Common;

namespace VStorage.Domain.Buckets
{
    /// <summary>Bucket name: 3–63 chars, lower-case letters, digits and hyphens, no hyphen at either end.</summary>
    public sealed partial record BucketName
    {
        private BucketName(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public static Result<BucketName> Create(string? value) =>
            value is not null && Pattern().IsMatch(value) ? new BucketName(value) : BucketErrors.InvalidName;

        /// <summary>Rehydrates a value that was validated before it was stored.</summary>
        public static BucketName FromStorage(string value) => new(value);

        public override string ToString() => Value;

        [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")]
        private static partial Regex Pattern();
    }
}
