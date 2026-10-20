using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VStorage.Application.Abstractions;
using VStorage.Application.Features.DownloadLinks;
using VStorage.Application.Features.Objects;
using VStorage.Application.Options;
using VStorage.Domain.Principals;

namespace VStorage.Application.Tests.DownloadLinks
{
    /// <summary>The paths of DownloadLinkService that are decided before the database (the rest: integration tests).</summary>
    public sealed class DownloadLinkServiceTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        private static readonly CurrentPrincipal Caller = new(Guid.NewGuid(), PrincipalKind.User, "vgplanner-api", false, false);

        private readonly IDownloadTokenProtector _protector = Substitute.For<IDownloadTokenProtector>();
        private readonly IVStorageDbContext _db = Substitute.For<IVStorageDbContext>();
        private readonly DownloadLinkService _service;

        public DownloadLinkServiceTests()
        {
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);
            var objects = new ObjectReadService(_db, Substitute.For<IBlobStore>(), NullLogger<ObjectReadService>.Instance);
            _service = new DownloadLinkService(
                objects,
                _protector,
                clock,
                Microsoft.Extensions.Options.Options.Create(new DownloadLinkOptions { DefaultLifetimeSeconds = 300, MaxLifetimeSeconds = 3600 }),
                Microsoft.Extensions.Options.Options.Create(new VStorageOptions { PublicBaseUrl = "https://files.test/" }));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(3601)]
        public async Task Create_rejects_a_lifetime_outside_1_to_max(int seconds)
        {
            var result = await _service.CreateAsync(Caller, Guid.NewGuid(), new CreateDownloadLinkRequest(seconds, null), TestContext.Current.CancellationToken);

            result.Error.Code.Should().Be("invalid-lifetime");
            _db.ReceivedCalls().Should().BeEmpty();
        }

        [Fact]
        public async Task Create_rejects_an_unknown_disposition()
        {
            var result = await _service.CreateAsync(Caller, Guid.NewGuid(), new CreateDownloadLinkRequest(null, "download"), TestContext.Current.CancellationToken);

            result.Error.Should().Be(DownloadLinkErrors.InvalidDisposition);
        }

        [Fact]
        public async Task Open_with_an_unknown_token_is_not_found()
        {
            _protector.Unprotect("bad").Returns((DownloadToken?)null);

            var result = await _service.OpenAsync("bad", TestContext.Current.CancellationToken);

            result.Error.Should().Be(DownloadLinkErrors.NotFound);
        }

        [Fact]
        public async Task Open_at_or_after_expiry_is_gone_without_touching_the_database()
        {
            _protector.Unprotect("old").Returns(new DownloadToken(Guid.NewGuid(), Caller.Id, false, Now));

            var result = await _service.OpenAsync("old", TestContext.Current.CancellationToken);

            result.Error.Should().Be(DownloadLinkErrors.Expired);
            _db.ReceivedCalls().Should().BeEmpty();
        }
    }
}
