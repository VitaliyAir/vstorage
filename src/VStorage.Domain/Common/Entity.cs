namespace VStorage.Domain.Common
{
    /// <summary>Marker for domain events. They are written to outbox_messages in the same transaction as the change.</summary>
    public interface IDomainEvent
    {
    }

    /// <summary>Base class of aggregates: a Guid id (UUIDv7) and the domain events raised since the last save.</summary>
    public abstract class Entity
    {
        private readonly List<IDomainEvent> _domainEvents = [];

        protected Entity(Guid id)
        {
            Id = id;
        }

        // For EF Core materialization.
        protected Entity()
        {
        }

        public Guid Id { get; private set; }

        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

        public void ClearDomainEvents() => _domainEvents.Clear();

        protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    }
}
