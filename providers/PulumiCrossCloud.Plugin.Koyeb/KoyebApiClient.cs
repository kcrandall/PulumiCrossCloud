using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Koyeb;

/// <summary>
/// Client for the Koyeb services and deployments API. Definitions are kept as JSON so an update
/// sends back every field Koyeb returned, including ones this client does not know.
/// </summary>
internal sealed class KoyebApiClient(
    HttpClient http,
    string apiToken,
    Uri baseUri,
    Func<TimeSpan, CancellationToken, Task>? delay = null
) : JsonApiClient("Koyeb", http, baseUri, new AuthenticationHeaderValue("Bearer", apiToken), delay)
{
    public static readonly Uri DefaultBaseUri = new("https://app.koyeb.com/");

    /// <summary>
    /// The service object (<c>{"service": {...}}</c> unwrapped), or null when it does not exist.
    /// </summary>
    public async Task<JsonObject?> GetServiceAsync(string serviceId, CancellationToken ct) =>
        (
            await SendAsync(
                HttpMethod.Get,
                $"v1/services/{Uri.EscapeDataString(serviceId)}",
                null,
                KoyebJson.Default.JsonObject,
                ct,
                allowNotFound: true
            )
        )?["service"] as JsonObject;

    public async Task<JsonObject> GetDeploymentAsync(string deploymentId, CancellationToken ct) =>
        (
            await SendAsync(
                HttpMethod.Get,
                $"v1/deployments/{Uri.EscapeDataString(deploymentId)}",
                null,
                KoyebJson.Default.JsonObject,
                ct
            )
        )?["deployment"] as JsonObject
        ?? throw new InvalidOperationException($"Koyeb returned no deployment {deploymentId}.");

    /// <summary>
    /// Redeploys the service with a new definition, reusing the last build.
    /// </summary>
    public async Task<JsonObject> UpdateDefinitionAsync(
        string serviceId,
        JsonObject definition,
        CancellationToken ct
    ) =>
        (
            await SendAsync(
                HttpMethod.Patch,
                $"v1/services/{Uri.EscapeDataString(serviceId)}",
                new JsonObject { ["definition"] = definition, ["skip_build"] = true },
                KoyebJson.Default.JsonObject,
                ct
            )
        )?["service"] as JsonObject
        ?? throw new InvalidOperationException($"Koyeb returned no service for {serviceId}.");
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JsonObject))]
internal sealed partial class KoyebJson : JsonSerializerContext;
