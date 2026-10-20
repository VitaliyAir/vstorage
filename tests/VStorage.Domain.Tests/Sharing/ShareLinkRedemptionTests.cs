using VStorage.Domain.Events;
using VStorage.Domain.Sharing;

namespace VStorage.Domain.Tests.Sharing
{
    public sealed class ShareLinkRedemptionTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void RecordRedemption_raises_ShareLinkRedeemed_without_changing_the_counter()
        {
            var recipient = Guid.NewGuid();
            var link = ShareLink.Create(Guid.NewGuid(), "abcdEFGH", Guid.NewGuid(), [recipient], Now.AddDays(1), 2, Now).Value;
            link.ClearDomainEvents();

            link.RecordRedemption(recipient);

            link.Downloads.Should().Be(0);
            link.DomainEvents.Should().ContainSingle()
                .Which.Should().Be(new ShareLinkRedeemed(link.Id, link.ObjectId, recipient));
        }
    }
}
