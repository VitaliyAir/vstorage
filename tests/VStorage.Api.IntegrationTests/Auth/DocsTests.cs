using System.Net;

namespace VStorage.Api.IntegrationTests.Auth
{
    [Collection(ApiCollection.Name)]
    public sealed class DocsTests(ApiFactory factory)
    {
        [Fact]
        public async Task OpenApi_document_is_anonymous_and_offers_keycloak_sign_in()
        {
            using var client = factory.CreateClient();

            var document = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

            document.Should().Contain("\"Keycloak\"")
                .And.Contain("authorizationCode")
                .And.Contain(TestTokens.Issuer + "/protocol/openid-connect/auth")
                .And.Contain("/api/v1/me");
        }

        [Fact]
        public async Task Scalar_ui_is_served()
        {
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/scalar/v1", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        }
    }
}
