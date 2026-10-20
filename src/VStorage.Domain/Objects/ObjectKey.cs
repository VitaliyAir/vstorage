using System.Text;
using VStorage.Domain.Common;

namespace VStorage.Domain.Objects
{
    /// <summary>
    /// Object path, e.g. "photos/2026/cat.jpg": valid UTF-8, 1–1024 bytes, "/"-separated non-empty segments
    /// (no leading, trailing or double slash), no "." or ".." segments, no backslash, no control characters.
    /// The key is never used to build a disk path.
    /// </summary>
    public sealed record ObjectKey
    {
        public const int MaxBytes = 1024;

        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private ObjectKey(string value)
        {
            Value = value;
        }

        public string Value { get; }

        public static Result<ObjectKey> Create(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return ObjectErrors.InvalidKey("Key is required.");
            }

            int byteCount;
            try
            {
                byteCount = StrictUtf8.GetByteCount(value);
            }
            catch (ArgumentException)
            {
                return ObjectErrors.InvalidKey("Key is not valid UTF-8.");
            }

            if (byteCount > MaxBytes)
            {
                return ObjectErrors.InvalidKey($"Key must not exceed {MaxBytes} bytes.");
            }

            if (value.Any(c => char.IsControl(c) || c == '\\'))
            {
                return ObjectErrors.InvalidKey("Key must not contain control characters or backslashes.");
            }

            if (value.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
            {
                return ObjectErrors.InvalidKey("Key segments must not be empty, '.' or '..' (no leading, trailing or double '/').");
            }

            return new ObjectKey(value);
        }

        /// <summary>Rehydrates a value that was validated before it was stored.</summary>
        public static ObjectKey FromStorage(string value) => new(value);

        public override string ToString() => Value;
    }
}
