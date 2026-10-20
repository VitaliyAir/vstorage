namespace VStorage.Application.Abstractions
{
    /// <summary>Random base62 slugs for /s/{slug} (FR-S4, SEC-4: RandomNumberGenerator).</summary>
    public interface ISlugGenerator
    {
        string Create(int length);
    }
}
