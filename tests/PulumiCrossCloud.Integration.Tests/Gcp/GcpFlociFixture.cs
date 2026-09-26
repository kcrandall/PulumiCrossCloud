using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Pulumi.Automation;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Gcp;
using PulumiCrossCloud.Integration.Tests.Infrastructure;

namespace PulumiCrossCloud.Integration.Tests.Gcp;

/// <summary>
/// Deploys every GCP resource floci-gcp can host through <see cref="GcpCloudResourceFactory"/>
/// once, and destroys it after the last test in <see cref="GcpFlociTests"/>.
/// </summary>
public sealed class GcpFlociFixture : IAsyncLifetime
{
    public const string Project = "floci-it";
    public const string Region = "us-central1";
    public const string BucketName = "floci-it-bucket";
    public const string PublicBucketName = "floci-it-public-bucket";
    public const string SecretName = "it-secret";
    public const string SecretValue = "floci-gcp-secret-value";
    public const string ServiceAccountId = "floci-it-sa";
    public const int QueueAckDeadlineSeconds = 45;
    public const int StreamRetentionHours = 48;

    // Custom endpoints for every API the deployed resources call, in the shape the provider
    // expects (floci-gcp mirrors the googleapis.com paths); anything missing would reach real GCP.
    private static readonly Dictionary<string, string> EndpointPaths = new()
    {
        ["storageCustomEndpoint"] = "/storage/v1/",
        ["pubsubCustomEndpoint"] = "/v1/",
        ["secretManagerCustomEndpoint"] = "/v1/",
        ["iamCustomEndpoint"] = "/",
        ["iamBetaCustomEndpoint"] = "/v1/",
        ["serviceUsageCustomEndpoint"] = "/v1/",
        ["resourceManagerCustomEndpoint"] = "/v1/",
    };

    private FlociEmulator? _emulator;

    public PulumiStack Stack { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _emulator = await FlociEmulator.StartGcpAsync();
        Http = new HttpClient { BaseAddress = new Uri(_emulator.Endpoint) };

        Dictionary<string, string> config = new()
        {
            ["gcp:project"] = Project,
            ["gcp:region"] = Region,
            // floci-gcp accepts any bearer token.
            ["gcp:accessToken"] = "floci",
        };
        foreach (var (key, path) in EndpointPaths)
            config[$"gcp:{key}"] = _emulator.Endpoint + path;

        Stack = await PulumiStack.CreateAsync(
            "pulumicrosscloud-gcp-floci",
            PulumiFn.Create(Program),
            config,
            // The provider otherwise validates the region against the real Compute API.
            new Dictionary<string, string?> { ["PULUMI_GCP_SKIP_REGION_VALIDATION"] = "true" }
        );
        await Stack.UpAsync();
    }

    private static IDictionary<string, object?> Program()
    {
        var factory = new GcpCloudResourceFactory();

        factory.CreateBucket(
            BucketName,
            versioningEnabled: true,
            corsRules:
            [
                new CorsRule(["https://app.floci-it.test"], ["GET", "PUT"], ["*"], ["ETag"], 600),
            ]
        );
        factory.CreateBucket(PublicBucketName, isPublic: true);

        var topic = factory.CreateTopic("it-topic");
        var queue = factory.CreateQueue(
            "it-queue",
            new QueueArgs
            {
                SubscribeTo = topic,
                VisibilityTimeoutSeconds = QueueAckDeadlineSeconds,
            }
        );
        var standaloneQueue = factory.CreateQueue("it-standalone-queue");
        var stream = factory.CreateStream(
            "it-stream",
            new StreamArgs { RetentionHours = StreamRetentionHours }
        );

        var secret = factory.CreateSecret(SecretName, SecretValue);
        var serviceAccount = factory.CreateRole(ServiceAccountId, assumeRolePolicy: "");

        return new Dictionary<string, object?>
        {
            ["topicId"] = topic.SendEndpoint,
            ["queueSubscriptionId"] = queue.ReceiveEndpoint,
            ["standaloneTopicId"] = standaloneQueue.SendEndpoint,
            ["standaloneSubscriptionId"] = standaloneQueue.ReceiveEndpoint,
            ["streamTopicId"] = stream.SendEndpoint,
            ["secretName"] = secret.Arn,
            ["serviceAccountEmail"] = serviceAccount.Arn,
        };
    }

    public async Task<JsonNode> GetJsonAsync(string path)
    {
        using var response = await Http.GetAsync(path);
        return await ReadJsonAsync(response);
    }

    public async Task<JsonNode> PostJsonAsync(string path, object body)
    {
        using var response = await Http.PostAsJsonAsync(path, body);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}: {content}"
        );
        return JsonNode.Parse(content) ?? new JsonObject();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Stack is not null)
                await Stack.DisposeAsync();
        }
        finally
        {
            Http?.Dispose();
            if (_emulator is not null)
                await _emulator.DisposeAsync();
        }
    }
}
