using System.Net;

namespace VStorage.Api.IntegrationTests
{
    [Collection(ApiCollection.Name)]
    public sealed class HealthTests(ApiFactory factory)
    {
        private const string RequestIdHeader = "X-Request-Id";

        [Fact]
        public async Task Live_IsHealthy_AndGetsGeneratedRequestId()
        {
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.GetValues(RequestIdHeader).Single().Should().MatchRegex("^[0-9a-f]{32}$");
        }

        [Fact]
        public async Task Live_EchoesValidRequestId()
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
            request.Headers.Add(RequestIdHeader, "consumer-req-123");

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            response.Headers.GetValues(RequestIdHeader).Single().Should().Be("consumer-req-123");
        }

        [Fact]
        public async Task Live_ReplacesUnsafeRequestId()
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
            request.Headers.TryAddWithoutValidation(RequestIdHeader, "<script>alert(1)</script>");

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            response.Headers.GetValues(RequestIdHeader).Single().Should().MatchRegex("^[0-9a-f]{32}$");
        }
    }
}
