using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace VStorage.Infrastructure.Persistence
{
    public static class MigrationExtensions
    {
        /// <summary>Applies pending EF Core migrations; creates the database if it does not exist.</summary>
        public static async Task ApplyMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        {
            await using var scope = services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<VStorageDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
    }
}
