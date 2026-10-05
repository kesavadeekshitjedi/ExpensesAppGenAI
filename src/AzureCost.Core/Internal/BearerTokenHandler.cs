using Azure.Core;
using Microsoft.Extensions.Options;

namespace AzureCost.Core.Internal;

/// <summary>
/// Attaches an ARM bearer token to every outgoing request. The underlying
/// <see cref="TokenCredential"/> caches and refreshes tokens, so this just asks for one.
/// </summary>
internal sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly TokenCredential _credential;
    private readonly string _scope;

    public BearerTokenHandler(TokenCredential credential, IOptions<AzureCostOptions> options)
    {
        _credential = credential;
        _scope = options.Value.ArmScope;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = new TokenRequestContext([_scope]);
        AccessToken token = await _credential
            .GetTokenAsync(context, cancellationToken)
            .ConfigureAwait(false);

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
