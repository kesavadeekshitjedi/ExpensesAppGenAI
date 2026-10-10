using System.Net;
using Microsoft.Extensions.Options;

namespace AzureCost.Core.Internal;

/// <summary>
/// Retries throttled (HTTP 429) and transient (502/503/504) ARM responses with backoff.
///
/// Cost Management throttles hard: when the UI fires a burst of queries — the user flicking
/// between subscriptions, periods or resource groups — the service answers 429 and the app
/// would otherwise surface a bare "Too many requests" error (see the throttling issue in
/// <c>docs/ISSUES.md</c>). This handler sits <b>outside</b> <see cref="BearerTokenHandler"/>
/// so each attempt gets a freshly-stamped bearer token, honours a server-supplied
/// <c>Retry-After</c>, and otherwise backs off exponentially with jitter.
///
/// Because an <see cref="HttpRequestMessage"/> (and its content stream) can only be sent once,
/// each attempt is sent on a fresh clone of the original request.
/// </summary>
internal sealed class ThrottleRetryHandler : DelegatingHandler
{
    private readonly AzureCostOptions _options;

    public ThrottleRetryHandler(IOptions<AzureCostOptions> options)
    {
        _options = options.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _options.MaxRetries);

        // Buffer the body once so every clone can re-read it.
        byte[]? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var attempt = 0; ; attempt++)
        {
            using var clone = CloneRequest(request, body);
            var response = await base.SendAsync(clone, cancellationToken).ConfigureAwait(false);

            if (!IsRetryable(response.StatusCode) || attempt >= maxRetries)
            {
                return response;
            }

            var delay = GetRetryDelay(response, attempt);
            response.Dispose();

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetryable(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => true, // 429 — throttled
        HttpStatusCode.BadGateway => true,      // 502
        HttpStatusCode.ServiceUnavailable => true, // 503
        HttpStatusCode.GatewayTimeout => true,  // 504
        _ => false,
    };

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        // A server-supplied Retry-After always wins.
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return delta;
            }

            if (retryAfter.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    return until;
                }
            }
        }

        // Exponential backoff with jitter: base * 2^attempt + up to 250ms.
        var backoffMs = _options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitterMs = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(backoffMs + jitterMs);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request, byte[]? body)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return clone;
    }
}
