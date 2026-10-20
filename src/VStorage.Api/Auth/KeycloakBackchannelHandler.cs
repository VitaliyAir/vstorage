namespace VStorage.Api.Auth
{
    /// <summary>
    /// Docker: the API reaches Keycloak at another address than the browser does. Tokens and Keycloak's own URLs say
    /// "http://localhost:8083", which inside a container is the container itself. Requests of this API to the public
    /// Keycloak origin go to <c>VStorage:Auth:InternalAuthority</c> instead (e.g. http://host.docker.internal:8083),
    /// with the public Host header, so Keycloak answers exactly as it does to the browser (same issuer and URLs).
    /// </summary>
    public sealed class KeycloakBackchannelHandler : DelegatingHandler
    {
        private readonly Uri _publicOrigin;
        private readonly Uri _internalOrigin;

        private KeycloakBackchannelHandler(Uri publicOrigin, Uri internalOrigin)
            : base(new SocketsHttpHandler())
        {
            _publicOrigin = publicOrigin;
            _internalOrigin = internalOrigin;
        }

        /// <summary>A handler when <paramref name="internalAuthority"/> is set (Docker); null = talk to Keycloak directly.</summary>
        public static HttpMessageHandler? Create(string? publicAuthority, string? internalAuthority)
        {
            if (string.IsNullOrWhiteSpace(internalAuthority) || string.IsNullOrWhiteSpace(publicAuthority))
            {
                return null;
            }

            return new KeycloakBackchannelHandler(new Uri(publicAuthority), new Uri(internalAuthority));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri
                && Uri.Compare(uri, _publicOrigin, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
            {
                request.Headers.Host = _publicOrigin.Authority;
                request.RequestUri = new UriBuilder(uri)
                {
                    Scheme = _internalOrigin.Scheme,
                    Host = _internalOrigin.Host,
                    Port = _internalOrigin.Port
                }.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
