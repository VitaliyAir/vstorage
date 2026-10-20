using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VStorage.Application.Abstractions;
using VStorage.Domain.Common;

namespace VStorage.Infrastructure.Persistence
{
    /// <summary>
    /// Before SaveChanges: turns the domain events of tracked aggregates into outbox_messages rows,
    /// so events and state changes are committed in the same transaction.
    /// </summary>
    internal sealed class OutboxInterceptor(IClock clock, IEventContext eventContext) : SaveChangesInterceptor
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            AddOutboxMessages(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            AddOutboxMessages(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void AddOutboxMessages(DbContext? context)
        {
            if (context is null)
            {
                return;
            }

            var aggregates = context.ChangeTracker.Entries<Entity>()
                .Select(entry => entry.Entity)
                .Where(entity => entity.DomainEvents.Count > 0)
                .ToList();
            if (aggregates.Count == 0)
            {
                return;
            }

            var occurredAt = clock.UtcNow;
            var requestContext = JsonSerializer.SerializeToNode(eventContext.Current, JsonOptions);

            foreach (var aggregate in aggregates)
            {
                foreach (var domainEvent in aggregate.DomainEvents)
                {
                    var payload = new JsonObject
                    {
                        ["data"] = JsonSerializer.SerializeToNode(domainEvent, domainEvent.GetType(), JsonOptions),
                        ["context"] = requestContext?.DeepClone()
                    };
                    context.Set<OutboxMessage>().Add(new OutboxMessage
                    {
                        Type = domainEvent.GetType().Name,
                        Payload = payload.ToJsonString(),
                        OccurredAt = occurredAt
                    });
                }

                aggregate.ClearDomainEvents();
            }
        }
    }
}
