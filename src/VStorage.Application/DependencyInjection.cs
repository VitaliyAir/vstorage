using Microsoft.Extensions.DependencyInjection;
using VStorage.Application.Features.Admin;
using VStorage.Application.Features.Buckets;
using VStorage.Application.Features.DownloadLinks;
using VStorage.Application.Features.Objects;
using VStorage.Application.Features.Principals;
using VStorage.Application.Features.ShareLinks;

namespace VStorage.Application
{
    public static class DependencyInjection
    {
        /// <summary>Registers the use-case services (plain classes, no MediatR).</summary>
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddScoped<PrincipalService>();
            services.AddScoped<BucketService>();
            services.AddScoped<MemberService>();
            services.AddScoped<AdminService>();
            services.AddScoped<UploadService>();
            services.AddScoped<ObjectReadService>();
            services.AddScoped<ObjectCommandService>();
            services.AddScoped<ObjectListingService>();
            services.AddScoped<DownloadLinkService>();
            services.AddScoped<ShareLinkService>();
            services.AddScoped<ShareLinkRedeemService>();
            return services;
        }
    }
}
