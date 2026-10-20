using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VStorage.Application.Abstractions;
using VStorage.Application.Options;
using VStorage.Infrastructure.Common;
using VStorage.Infrastructure.Persistence;
using VStorage.Infrastructure.Queries;
using VStorage.Infrastructure.Security;
using VStorage.Infrastructure.Storage;

namespace VStorage.Infrastructure
{
    public static class DependencyInjection
    {
        public const string ConnectionStringName = "VStorage";

        /// <summary>
        /// Registers persistence (PostgreSQL + outbox), the local-disk blob store and the Data Protection key ring;
        /// workers follow in stage S9.
        /// </summary>
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is missing. In src/VStorage.Api run: " +
                    $"dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"<connection string>\"");
            }

            services.AddSingleton<IClock, SystemClock>();
            services.TryAddScoped<IEventContext, EmptyEventContext>();
            services.AddScoped<OutboxInterceptor>();

            services.AddDbContext<VStorageDbContext>((serviceProvider, options) => options
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(serviceProvider.GetRequiredService<OutboxInterceptor>()));
            services.AddScoped<IVStorageDbContext>(serviceProvider => serviceProvider.GetRequiredService<VStorageDbContext>());
            services.AddScoped<IObjectListingQuery, ObjectListingQuery>();

            services.AddSingleton<BlobPaths>();
            services.AddSingleton<IBlobStore, FileSystemBlobStore>();

            // Key ring in VStorage:DataProtection:KeysPath (same as PersistKeysToFileSystem, but the path is resolved
            // against the content root). Keys are stored unencrypted: protect the directory with file permissions (SEC-4).
            services.AddDataProtection().SetApplicationName("VStorage");
            services.AddOptions<KeyManagementOptions>()
                .Configure<IOptions<KeyRingOptions>, IHostEnvironment, ILoggerFactory>((keys, keyRing, environment, loggerFactory) =>
                {
                    var directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, keyRing.Value.KeysPath));
                    keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(directory), loggerFactory);
                });
            services.AddSingleton<IDownloadTokenProtector, DataProtectionDownloadTokenProtector>();
            services.AddSingleton<ISlugGenerator, SlugGenerator>();

            // Part of /health/ready (mapped in stage S10).
            services.AddHealthChecks().AddDbContextCheck<VStorageDbContext>("database", tags: ["ready"]);

            return services;
        }
    }
}
