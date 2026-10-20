using VStorage.Api.Auth;
using VStorage.Application.Options;

namespace VStorage.Api.Configuration
{
    internal static class OptionsRegistration
    {
        /// <summary>Binds every VStorage settings section and validates it at startup (fail fast on bad config).</summary>
        public static IServiceCollection AddVStorageOptions(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddValidatedOptions<VStorageOptions>(configuration, VStorageOptions.SectionName);
            services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.SectionName);
            services.AddValidatedOptions<ShareLinkOptions>(configuration, ShareLinkOptions.SectionName);
            services.AddValidatedOptions<DownloadLinkOptions>(configuration, DownloadLinkOptions.SectionName);
            services.AddValidatedOptions<KeyRingOptions>(configuration, KeyRingOptions.SectionName);
            services.AddValidatedOptions<CleanupOptions>(configuration, CleanupOptions.SectionName);
            services.AddValidatedOptions<AuthOptions>(configuration, AuthOptions.SectionName);
            services.AddValidatedOptions<DocsOptions>(configuration, DocsOptions.SectionName);
            return services;
        }

        private static void AddValidatedOptions<TOptions>(this IServiceCollection services, IConfiguration configuration, string sectionName)
            where TOptions : class
        {
            services.AddOptions<TOptions>()
                .Bind(configuration.GetSection(sectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }
    }
}
