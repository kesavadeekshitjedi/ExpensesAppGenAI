using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using AzureCost.Core;
using AzureCost.Core.Internal;
using Microsoft.Extensions.Options;
using Xunit;

namespace AzureCost.Core.Tests;

public class ThrottleRetryHandlerTests
{
    [Fact]
    public async Task Retries_throttled_response_then_returns_success()
    {
        var stub = new StubHandler(
            _ => Respond(HttpStatusCode.TooManyRequests),
            _ => Respond(HttpStatusCode.TooManyRequests),
            _ => Respond(HttpStatusCode.OK));

        using var response = await SendAsync(stub, Options());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.Calls); // two 429s + the success
    }

    [Fact]
    public async Task Gives_up_after_max_retries_and_returns_last_response()
    {
        var stub = new StubHandler(_ => Respond(HttpStatusCode.TooManyRequests));

        using var response = await SendAsync(stub, Options(maxRetries: 2));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, stub.Calls); // initial attempt + 2 retries
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]        // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)] // 503
    [InlineData(HttpStatusCode.GatewayTimeout)]     // 504
    public async Task Retries_transient_server_errors(HttpStatusCode transient)
    {
        var stub = new StubHandler(
            _ => Respond(transient),
            _ => Respond(HttpStatusCode.OK));

        using var response = await SendAsync(stub, Options());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Calls);
    }

    [Fact]
    public async Task Does_not_retry_non_retryable_status()
    {
        var stub = new StubHandler(_ => Respond(HttpStatusCode.BadRequest));

        using var response = await SendAsync(stub, Options());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, stub.Calls); // no retry on 400
    }

    [Fact]
    public async Task Honours_Retry_After_header_delay()
    {
        var retryAfter = TimeSpan.FromMilliseconds(400);
        var stub = new StubHandler(
            _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
                return Task.FromResult(r);
            },
            _ => Respond(HttpStatusCode.OK));

        // Base backoff is tiny; if Retry-After is honoured the wait is ~400ms.
        var sw = Stopwatch.StartNew();
        using var response = await SendAsync(stub, Options());
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(300),
            $"expected to wait for Retry-After, waited {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Resends_request_body_on_each_retry()
    {
        const string body = "{\"type\":\"ActualCost\"}";
        var stub = new StubHandler(
            _ => Respond(HttpStatusCode.TooManyRequests),
            _ => Respond(HttpStatusCode.OK));

        using var handler = new ThrottleRetryHandler(Options()) { InnerHandler = stub };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://management.azure.com/query")
        {
            Content = new StringContent(body),
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.ReceivedBodies.Count);
        Assert.All(stub.ReceivedBodies, b => Assert.Equal(body, b)); // body survived the retry
    }

    private static IOptions<AzureCostOptions> Options(int maxRetries = 4) =>
        Microsoft.Extensions.Options.Options.Create(new AzureCostOptions
        {
            MaxRetries = maxRetries,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1), // keep tests fast
        });

    private static async Task<HttpResponseMessage> SendAsync(
        StubHandler stub, IOptions<AzureCostOptions> options)
    {
        using var handler = new ThrottleRetryHandler(options) { InnerHandler = stub };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com/");
        return await invoker.SendAsync(request, CancellationToken.None);
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status) =>
        Task.FromResult(new HttpResponseMessage(status));

    /// <summary>Returns queued responders in order; once the last one is reached it repeats,
    /// so a single responder can answer every call. Records each request body it sees.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _responders;

        public StubHandler(params Func<HttpRequestMessage, Task<HttpResponseMessage>>[] responders)
        {
            _responders = new Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>>(responders);
        }

        public int Calls { get; private set; }

        public List<string?> ReceivedBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedBodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));

            var responder = _responders.Count > 1 ? _responders.Dequeue() : _responders.Peek();
            return await responder(request);
        }
    }
}
