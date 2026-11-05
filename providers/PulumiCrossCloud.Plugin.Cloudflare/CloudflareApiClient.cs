using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Cloudflare;

/// <summary>
/// Client for Cloudflare's account API token endpoints (<c>/accounts/{id}/tokens</c>).
/// </summary>
internal sealed class CloudflareApiClient(
    HttpClient http,
    string apiToken,
    Uri baseUri,
    Func<TimeSpan, CancellationToken, Task>? delay = null
)
    : JsonApiClient(
        "Cloudflare",
        http,
        baseUri,
        new AuthenticationHeaderValue("Bearer", apiToken),
        delay
    )
{
    public static readonly Uri DefaultBaseUri = new("https://api.cloudflare.com/client/v4/");

    /// <summary>
    /// Creates a token. The response is the only time Cloudflare returns its value.
    /// </summary>
    public async Task<CloudflareToken> CreateTokenAsync(
        string accountId,
        TokenPolicy policy,
        CancellationToken ct
    ) =>
        Result(
            await SendAsync(
                HttpMethod.Post,
                TokensPath(accountId),
                Body(policy),
                CloudflareJson.Default.CloudflareTokenEnvelope,
                ct
            )
        );

    public async Task<CloudflareToken?> GetTokenAsync(
        string accountId,
        string tokenId,
        CancellationToken ct
    )
    {
        var envelope = await SendAsync(
            HttpMethod.Get,
            TokenPath(accountId, tokenId),
            null,
            CloudflareJson.Default.CloudflareTokenEnvelope,
            ct,
            allowNotFound: true
        );
        return envelope is null ? null : Result(envelope);
    }

    /// <summary>
    /// Replaces the token's name and policy; its value does not change.
    /// </summary>
    public async Task<CloudflareToken> UpdateTokenAsync(
        string accountId,
        string tokenId,
        TokenPolicy policy,
        CancellationToken ct
    )
    {
        var body = Body(policy);
        body["status"] = "active";
        return Result(
            await SendAsync(
                HttpMethod.Put,
                TokenPath(accountId, tokenId),
                body,
                CloudflareJson.Default.CloudflareTokenEnvelope,
                ct
            )
        );
    }

    /// <summary>
    /// Revokes a token. Returns false when it no longer exists.
    /// </summary>
    public Task<bool> DeleteTokenAsync(string accountId, string tokenId, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, TokenPath(accountId, tokenId), ct, allowNotFound: true);

    private CloudflareToken Result(CloudflareTokenEnvelope? envelope) =>
        envelope?.Result
        ?? throw new InvalidOperationException(
            "Cloudflare returned a token response without a result."
        );

    private static JsonObject Body(TokenPolicy policy) =>
        new()
        {
            ["name"] = policy.Name,
            ["policies"] = new JsonArray(
                new JsonObject
                {
                    ["effect"] = "allow",
                    ["resources"] = new JsonObject(
                        policy.Resources.Select(pair =>
                            KeyValuePair.Create(pair.Key, (JsonNode?)JsonValue.Create(pair.Value))
                        )
                    ),
                    ["permission_groups"] = new JsonArray(
                        [
                            .. policy.PermissionGroupIds.Select(id =>
                                (JsonNode)new JsonObject { ["id"] = id }
                            ),
                        ]
                    ),
                }
            ),
        };

    private static string TokensPath(string accountId) =>
        $"accounts/{Uri.EscapeDataString(accountId)}/tokens";

    private static string TokenPath(string accountId, string tokenId) =>
        $"{TokensPath(accountId)}/{Uri.EscapeDataString(tokenId)}";
}

/// <summary>
/// A token's name and its single allow policy.
/// </summary>
internal sealed record TokenPolicy(
    string Name,
    IReadOnlyList<string> PermissionGroupIds,
    IReadOnlyDictionary<string, string> Resources
);

internal sealed record CloudflareTokenEnvelope
{
    public bool Success { get; init; }
    public CloudflareToken? Result { get; init; }
}

internal sealed record CloudflareToken
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>active, disabled or expired.</summary>
    public string Status { get; init; } = "";

    /// <summary>Only present when the token is created.</summary>
    public string? Value { get; init; }
    public List<CloudflarePolicy> Policies { get; init; } = [];
}

internal sealed record CloudflarePolicy
{
    public string Effect { get; init; } = "";
    public Dictionary<string, JsonElement> Resources { get; init; } = [];
    public List<CloudflarePermissionGroup> PermissionGroups { get; init; } = [];
}

internal sealed record CloudflarePermissionGroup
{
    public string Id { get; init; } = "";
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
)]
[JsonSerializable(typeof(CloudflareTokenEnvelope))]
internal sealed partial class CloudflareJson : JsonSerializerContext;
