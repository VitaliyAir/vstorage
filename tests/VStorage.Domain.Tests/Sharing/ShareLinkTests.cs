using VStorage.Domain.Events;
using VStorage.Domain.Sharing;

namespace VStorage.Domain.Tests.Sharing
{
    public sealed class ShareLinkTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        private static readonly Guid ObjectId = Guid.NewGuid();
        private static readonly Guid Creator = Guid.NewGuid();
        private static readonly Guid Recipient = Guid.NewGuid();

        [Fact]
        public void Without_recipients_the_link_is_public()
        {
            var link = Create([]);

            link.Kind.Should().Be(ShareLinkKind.Public);
            link.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShareLinkCreated>();
        }

        [Fact]
        public void With_recipients_the_link_is_addressed_and_deduplicated()
        {
            var link = Create([Recipient, Recipient]);

            link.Kind.Should().Be(ShareLinkKind.Addressed);
            link.Recipients.Should().ContainSingle();
            link.IsRecipient(Recipient).Should().BeTrue();
            link.IsRecipient(Creator).Should().BeFalse();
        }

        [Theory]
        [InlineData("short")]
        [InlineData("has-dash-1")]
        [InlineData("")]
        public void Invalid_slug_is_rejected(string slug)
        {
            ShareLink.Create(ObjectId, slug, Creator, [], Now.AddDays(1), null, Now).Error.Should().Be(ShareLinkErrors.InvalidSlug);
        }

        [Fact]
        public void Expiry_and_max_downloads_are_validated()
        {
            ShareLink.Create(ObjectId, "Abcdefgh12", Creator, [], Now, null, Now).Error.Should().Be(ShareLinkErrors.ExpiryInPast);
            ShareLink.Create(ObjectId, "Abcdefgh12", Creator, [], Now.AddDays(1), 0, Now).Error.Should().Be(ShareLinkErrors.InvalidMaxDownloads);
        }

        [Fact]
        public void State_changes_with_time_and_revocation()
        {
            var link = Create([]);

            link.StateAt(Now).Should().Be(ShareLinkState.Active);
            link.StateAt(Now.AddDays(1)).Should().Be(ShareLinkState.Expired);

            link.Revoke(Now).IsSuccess.Should().BeTrue();
            link.StateAt(Now).Should().Be(ShareLinkState.Revoked);
            link.Revoke(Now).Error.Should().Be(ShareLinkErrors.AlreadyRevoked);
        }

        private static ShareLink Create(Guid[] recipients) =>
            ShareLink.Create(ObjectId, "Abcdefgh12", Creator, recipients, Now.AddDays(1), null, Now).Value;
    }
}
