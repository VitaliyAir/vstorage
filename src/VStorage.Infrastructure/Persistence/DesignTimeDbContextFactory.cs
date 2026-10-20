using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VStorage.Infrastructure.Persistence
{
    /// <summary>
    /// Used only by `dotnet ef migrations add` (no connection is opened, no secrets needed).
    /// Migrations are applied by the API at startup (Database:ApplyMigrationsOnStartup).
    /// </summary>
    public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VStorageDbContext>
    {
        public VStorageDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<VStorageDbContext>()
                .UseNpgsql("Host=localhost;Database=vstoragedb_design")
                .UseSnakeCaseNamingConvention()
                .Options;
            return new VStorageDbContext(options);
        }
    }
}
