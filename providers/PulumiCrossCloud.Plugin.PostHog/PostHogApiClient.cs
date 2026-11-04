using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.PostHog;

/// <summary>
/// Client for PostHog's organization API (<c>/api/organizations/</c>) using a personal API key.
/// </summary>
internal sealed class PostHogApiClient(
    HttpClient http,
    string apiKey,
    Uri host,
    Func<TimeSpan, CancellationToken, Task>? delay = null
) : JsonApiClient("PostHog", http, host, new AuthenticationHeaderValue("Bearer", apiKey), delay)
{
    private const string Organizations = "api/organizations/";

    public async Task<PostHogOrganization> CreateOrganizationAsync(
        string name,
        CancellationToken ct
    ) =>
        (
            await SendAsync(
                HttpMethod.Post,
                Organizations,
                new JsonObject { ["name"] = name },
                PostHogJson.Default.PostHogOrganization,
                ct
            )
        )!;

    public Task<PostHogOrganization?> GetOrganizationAsync(string id, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Get,
            OrganizationPath(id),
            null,
            PostHogJson.Default.PostHogOrganization,
            ct,
            allowNotFound: true
        );

    public async Task<PostHogOrganization> RenameOrganizationAsync(
        string id,
        string name,
        CancellationToken ct
    ) =>
        (
            await SendAsync(
                HttpMethod.Patch,
                OrganizationPath(id),
                new JsonObject { ["name"] = name },
                PostHogJson.Default.PostHogOrganization,
                ct
            )
        )!;

    public Task<bool> DeleteOrganizationAsync(string id, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, OrganizationPath(id), ct, allowNotFound: true);

    /// <summary>
    /// Finds an organization the key can access by exact name.
    /// </summary>
    public async Task<PostHogOrganization?> FindOrganizationAsync(string name, CancellationToken ct)
    {
        string? path = $"{Organizations}?limit=100";
        while (path is not null)
        {
            var page = (
                await SendAsync(
                    HttpMethod.Get,
                    path,
                    null,
                    PostHogJson.Default.PostHogOrganizationPage,
                    ct
                )
            )!;
            var match = page.Results.FirstOrDefault(organization => organization.Name == name);
            if (match is not null)
                return match;

            // "next" is an absolute URL; keep its path and query relative to the configured host.
            path = page.Next is null ? null : new Uri(page.Next).PathAndQuery.TrimStart('/');
        }

        return null;
    }

    private static string OrganizationPath(string id) =>
        $"{Organizations}{Uri.EscapeDataString(id)}/";
}

internal sealed record PostHogOrganization
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Slug { get; init; }
}

internal sealed record PostHogOrganizationPage
{
    public List<PostHogOrganization> Results { get; init; } = [];
    public string? Next { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
)]
[JsonSerializable(typeof(PostHogOrganization))]
[JsonSerializable(typeof(PostHogOrganizationPage))]
internal sealed partial class PostHogJson : JsonSerializerContext;
