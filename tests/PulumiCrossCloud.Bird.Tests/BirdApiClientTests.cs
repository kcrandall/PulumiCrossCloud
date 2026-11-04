using System.Net;
using System.Text.Json.Nodes;
using PulumiCrossCloud.Bird.Provider;

namespace PulumiCrossCloud.Bird.Tests;

public sealed class BirdApiClientTests
{
    private const string ApiKey = "bk_us1_secret";

    [Fact]
    public async Task CreateDomainSendsAuthenticatedSnakeCaseRequestAndParsesRecords()
    {
        var handler = new FakeBirdHandler().Respond(HttpStatusCode.Created, BirdJson.Domain());
        var client = Client(handler);

        var domain = await client.CreateDomainAsync(
            new JsonObject
            {
                ["domain"] = "mail.example.com",
                ["return_path"] = new JsonObject { ["name"] = "bounce" },
            },
            CancellationToken.None
        );

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://us1.platform.bird.com/v1/email/domains", request.Uri.ToString());
        Assert.Equal($"Bearer {ApiKey}", request.Authorization);
        Assert.NotNull(request.IdempotencyKey);
        Assert.Equal("bounce", request.Body!["return_path"]!["name"]!.GetValue<string>());

        Assert.Equal("dom_1", domain.Id);
        Assert.Equal("send.mail.example.com", domain.Capabilities!.ReturnPath!.Domain);
        var record = Assert.Single(domain.DnsRecords);
        Assert.Equal("bird1._domainkey.mail.example.com", record.Host);
    }

    [Fact]
    public async Task RetriesRateLimitsWithTheSameIdempotencyKey()
    {
        var handler = new FakeBirdHandler()
            .Respond(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(7))
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.OK, BirdJson.Domain());
        List<TimeSpan> delays = [];
        var client = Client(handler, delays);

        await client.VerifyDomainAsync("dom_1", CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(handler.Requests.Select(request => request.IdempotencyKey).Distinct());
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task MissingDomainsAreReportedWithoutErrors()
    {
        var handler = new FakeBirdHandler()
            .Respond(HttpStatusCode.NotFound, BirdJson.Error("E04040", "Not found."))
            .Respond(HttpStatusCode.NotFound, BirdJson.Error("E04040", "Not found."));
        var client = Client(handler);

        Assert.Null(await client.GetDomainAsync("dom_gone", CancellationToken.None));
        Assert.False(await client.DeleteDomainAsync("dom_gone", CancellationToken.None));
    }

    [Fact]
    public async Task ErrorsIncludeTheBirdCodeAndRequestButNotTheApiKey()
    {
        var handler = new FakeBirdHandler().Respond(
            HttpStatusCode.Conflict,
            BirdJson.Error("E05001", "Domain already exists.")
        );
        var client = Client(handler);

        var error = await Assert.ThrowsAsync<BirdApiException>(() =>
            client.CreateDomainAsync(
                new JsonObject { ["domain"] = "mail.example.com" },
                CancellationToken.None
            )
        );

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("E05001", error.Code);
        Assert.Contains("Domain already exists.", error.Message);
        Assert.Contains("req_1", error.Message);
        Assert.DoesNotContain(ApiKey, error.Message);
    }

    [Theory]
    [InlineData("bk_us1_abc", "us1")]
    [InlineData("bm_eu1_abc", "eu1")]
    [InlineData("bk_ap9_abc", null)]
    [InlineData("not-a-bird-key", null)]
    [InlineData(null, null)]
    public void RegionComesFromTheApiKeyPrefix(string? apiKey, string? expected) =>
        Assert.Equal(expected, BirdRegions.FromApiKey(apiKey));

    private static BirdApiClient Client(FakeBirdHandler handler, List<TimeSpan>? delays = null) =>
        new(
            new HttpClient(handler),
            ApiKey,
            BirdRegions.ApiBaseUri("us1"),
            (delay, _) =>
            {
                delays?.Add(delay);
                return Task.CompletedTask;
            }
        );
}
