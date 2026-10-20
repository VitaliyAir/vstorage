using System.Security.Cryptography;
using VStorage.Application.Abstractions;

namespace VStorage.Infrastructure.Security
{
    /// <summary>Uniformly random base62 (no modulo bias): 12 characters ≈ 71 bits, 8 characters ≈ 47 bits.</summary>
    internal sealed class SlugGenerator : ISlugGenerator
    {
        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        public string Create(int length) => RandomNumberGenerator.GetString(Alphabet, length);
    }
}
