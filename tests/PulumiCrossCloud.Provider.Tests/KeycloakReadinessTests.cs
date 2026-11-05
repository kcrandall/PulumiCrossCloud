using System.Net;
using PulumiCrossCloud.Keycloak;

namespace PulumiCrossCloud.Provider.Tests;

public sealed class KeycloakReadinessTests
{
    [Fact]
    public async Task WaitsThroughRefusedConnectionsAndErrorsUntilReady()
    {
        var handler = new SequenceHandler(
            () => throw new HttpRequestException("Connection refused"),
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => new HttpResponseMessage(HttpStatusCode.OK)
        );
        List<TimeSpan> delays = [];

        await KeycloakReadiness.WaitAsync(
            "https://auth.example.com/",
            new HttpClient(handler),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            }
        );

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            uri => Assert.Equal("https://auth.example.com/health/ready", uri.ToString())
        );
        Assert.Equal([KeycloakReadiness.Interval, KeycloakReadiness.Interval], delays);
    }

    [Fact]
    public async Task TimesOutWithTheLastProblem()
    {
        var handler = new SequenceHandler(
            Enumerable
                .Repeat<Func<HttpResponseMessage>>(
                    () => new HttpResponseMessage(HttpStatusCode.BadGateway),
                    KeycloakReadiness.Attempts
                )
                .ToArray()
        );

        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            KeycloakReadiness.WaitAsync(
                "https://auth.example.com",
                new HttpClient(handler),
                (_, _) => Task.CompletedTask
            )
        );

        Assert.Contains("HTTP 502", error.Message);
        Assert.Equal(KeycloakReadiness.Attempts, handler.Requests.Count);
    }

    private sealed class SequenceHandler(params Func<HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private int _next;

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(responses[_next++]());
        }
    }
}
