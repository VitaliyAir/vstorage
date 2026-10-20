using System.ComponentModel.DataAnnotations;
using VStorage.Application.Options;

namespace VStorage.Application.Tests.Options
{
    public sealed class OptionsValidationTests
    {
        [Fact]
        public void Defaults_AreValid()
        {
            Validate(new StorageOptions()).Should().BeEmpty();
            Validate(new ShareLinkOptions()).Should().BeEmpty();
            Validate(new DownloadLinkOptions()).Should().BeEmpty();
            Validate(new CleanupOptions()).Should().BeEmpty();
        }

        [Fact]
        public void ShareLinks_DefaultLongerThanMax_IsInvalid()
        {
            var options = new ShareLinkOptions { DefaultExpiresIn = TimeSpan.FromDays(400), MaxExpiresIn = TimeSpan.FromDays(365) };

            Validate(options).Should().ContainSingle(r => r.MemberNames.Contains(nameof(ShareLinkOptions.DefaultExpiresIn)));
        }

        [Fact]
        public void DownloadLinks_DefaultLongerThanMax_IsInvalid()
        {
            var options = new DownloadLinkOptions { DefaultLifetimeSeconds = 600, MaxLifetimeSeconds = 300 };

            Validate(options).Should().ContainSingle();
        }

        [Fact]
        public void PublicBaseUrl_IsRequired()
        {
            Validate(new VStorageOptions()).Should().NotBeEmpty();
            Validate(new VStorageOptions { PublicBaseUrl = "https://files.example.com" }).Should().BeEmpty();
        }

        private static List<ValidationResult> Validate(object options)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }
    }
}
