using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Neon;

/// <summary>
/// Client for the Neon v2 API endpoints (computes) of a project.
/// </summary>
internal sealed class NeonApiClient(
    HttpClient http,
    string apiKey,
    Uri baseUri,
    Func<TimeSpan, CancellationToken, Task>? delay = null
) : JsonApiClient("Neon", http, baseUri, new AuthenticationHeaderValue("Bearer", apiKey), delay)
{
    public static readonly Uri DefaultBaseUri = new("https://console.neon.tech/api/v2/");

    public async Task<IReadOnlyList<NeonEndpoint>> ListEndpointsAsync(
        string projectId,
        CancellationToken ct
    ) =>
        (
            await SendAsync(
                HttpMethod.Get,
                $"projects/{Uri.EscapeDataString(projectId)}/endpoints",
                null,
                NeonJson.Default.NeonEndpointList,
                ct
            )
        )!.Endpoints;

    public async Task<NeonEndpoint?> GetEndpointAsync(
        string projectId,
        string endpointId,
        CancellationToken ct
    ) =>
        (
            await SendAsync(
                HttpMethod.Get,
                EndpointPath(projectId, endpointId),
                null,
                NeonJson.Default.NeonEndpointResponse,
                ct,
                allowNotFound: true
            )
        )?.Endpoint;

    public async Task<NeonEndpoint> UpdateEndpointAsync(
        string projectId,
        string endpointId,
        NeonComputeSettings settings,
        CancellationToken ct
    )
    {
        var endpoint = new JsonObject();
        if (settings.MinCu is { } minCu)
            endpoint["autoscaling_limit_min_cu"] = minCu;
        if (settings.MaxCu is { } maxCu)
            endpoint["autoscaling_limit_max_cu"] = maxCu;
        if (settings.SuspendTimeoutSeconds is { } suspend)
            endpoint["suspend_timeout_seconds"] = suspend;

        return (
            await SendAsync(
                HttpMethod.Patch,
                EndpointPath(projectId, endpointId),
                new JsonObject { ["endpoint"] = endpoint },
                NeonJson.Default.NeonEndpointResponse,
                ct
            )
        )!.Endpoint;
    }

    private static string EndpointPath(string projectId, string endpointId) =>
        $"projects/{Uri.EscapeDataString(projectId)}/endpoints/{Uri.EscapeDataString(endpointId)}";
}

/// <summary>
/// Compute settings to apply; null leaves a setting as it is.
/// </summary>
internal sealed record NeonComputeSettings(
    double? MinCu,
    double? MaxCu,
    int? SuspendTimeoutSeconds
);

internal sealed record NeonEndpoint
{
    public string Id { get; init; } = "";
    public string ProjectId { get; init; } = "";
    public string BranchId { get; init; } = "";

    /// <summary>read_write or read_only.</summary>
    public string Type { get; init; } = "";
    public double AutoscalingLimitMinCu { get; init; }
    public double AutoscalingLimitMaxCu { get; init; }
    public int SuspendTimeoutSeconds { get; init; }
}

internal sealed record NeonEndpointList
{
    public List<NeonEndpoint> Endpoints { get; init; } = [];
}

internal sealed record NeonEndpointResponse
{
    public NeonEndpoint Endpoint { get; init; } = new();
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
)]
[JsonSerializable(typeof(NeonEndpointList))]
[JsonSerializable(typeof(NeonEndpointResponse))]
internal sealed partial class NeonJson : JsonSerializerContext;
