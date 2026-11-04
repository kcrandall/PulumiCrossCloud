using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Bird;

/// <summary>
/// Client for Bird's sending-domain API (<c>/v1/email/domains</c>).
/// </summary>
internal sealed class BirdApiClient(
    HttpClient http,
    string apiKey,
    Uri baseUri,
    Func<TimeSpan, CancellationToken, Task>? delay = null
)
    : JsonApiClient(
        "Bird",
        http,
        baseUri,
        new AuthenticationHeaderValue("Bearer", apiKey),
        delay,
        sendIdempotencyKeys: true
    )
{
    private const string Domains = "v1/email/domains";

    public async Task<BirdDomain> CreateDomainAsync(JsonObject body, CancellationToken ct) =>
        (await SendAsync(HttpMethod.Post, Domains, body, BirdJson.Default.BirdDomain, ct))!;

    public Task<BirdDomain?> GetDomainAsync(string id, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Get,
            DomainPath(id),
            null,
            BirdJson.Default.BirdDomain,
            ct,
            allowNotFound: true
        );

    public async Task<BirdDomain> UpdateDomainAsync(
        string id,
        JsonObject body,
        CancellationToken ct
    ) =>
        (await SendAsync(HttpMethod.Patch, DomainPath(id), body, BirdJson.Default.BirdDomain, ct))!;

    public async Task<BirdDomain> VerifyDomainAsync(string id, CancellationToken ct) =>
        (
            await SendAsync(
                HttpMethod.Post,
                $"{DomainPath(id)}/verify",
                null,
                BirdJson.Default.BirdDomain,
                ct
            )
        )!;

    /// <summary>
    /// Deletes the domain. Returns false when it no longer exists.
    /// </summary>
    public Task<bool> DeleteDomainAsync(string id, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, DomainPath(id), ct, allowNotFound: true);

    /// <summary>
    /// Finds a domain by exact name, paging through Bird's substring search.
    /// </summary>
    public async Task<BirdDomain?> FindDomainAsync(string domain, CancellationToken ct)
    {
        string? cursor = null;
        do
        {
            var query = $"{Domains}?name={Uri.EscapeDataString(domain)}&limit=100";
            if (cursor is not null)
                query += $"&starting_after={Uri.EscapeDataString(cursor)}";

            var page = (
                await SendAsync(HttpMethod.Get, query, null, BirdJson.Default.BirdDomainList, ct)
            )!;
            var match = page.Data.FirstOrDefault(item =>
                string.Equals(item.Domain, domain, StringComparison.OrdinalIgnoreCase)
            );
            if (match is not null)
                return match;

            cursor = page.NextCursor;
        } while (cursor is not null);

        return null;
    }

    private static string DomainPath(string id) => $"{Domains}/{Uri.EscapeDataString(id)}";
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
)]
[JsonSerializable(typeof(BirdDomain))]
[JsonSerializable(typeof(BirdDomainList))]
internal sealed partial class BirdJson : JsonSerializerContext;
