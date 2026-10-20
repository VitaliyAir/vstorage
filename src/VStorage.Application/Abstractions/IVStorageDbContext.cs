using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VStorage.Domain.Buckets;
using VStorage.Domain.Objects;
using VStorage.Domain.Principals;
using VStorage.Domain.Sharing;

namespace VStorage.Application.Abstractions
{
    /// <summary>Unit of work over the VStorage tables. Domain events are written to the outbox on save.</summary>
    public interface IVStorageDbContext
    {
        DbSet<Principal> Principals { get; }

        DbSet<Bucket> Buckets { get; }

        DbSet<StoredObject> Objects { get; }

        DbSet<ShareLink> ShareLinks { get; }

        /// <summary>Explicit transactions (upload commit, delete with quota release).</summary>
        DatabaseFacade Database { get; }

        /// <summary>Used to drop half-applied changes after a failed commit.</summary>
        ChangeTracker ChangeTracker { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
