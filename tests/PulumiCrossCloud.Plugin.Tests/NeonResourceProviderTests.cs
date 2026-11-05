using System.Net;
using System.Text.Json.Nodes;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.Neon;

namespace PulumiCrossCloud.Plugin.Tests;

public sealed class NeonResourceProviderTests
{
    private const string Settings = EndpointSettingsResource.Type;

    private readonly FakeApiHandler _handler = new();
    private readonly List<TimeSpan> _delays = [];

    [Fact]
    public async Task CreateLeavesAComputeThatAlreadyMatchesUntouched()
    {
        // An existing stack moving from the earlier script: the compute already has the settings.
        var provider = await Configured();
        _handler.Respond(
            HttpStatusCode.OK,
            Endpoints(Endpoint("ep-1", min: 0.25, max: 0.25, suspend: 300))
        );

        var response = await Create(provider, ("minCu", 0.25), ("maxCu", 0.25), ("suspendTimeoutSeconds", 0));

        Assert.Equal("ep-1", response.Id);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://console.neon.tech/api/v2/projects/proj-1/endpoints",
            request.Uri.ToString()
        );
        Assert.Equal("Bearer napi_key", request.Authorization);
        var outputs = ProviderTesting.Map(response.Outputs);
        Assert.Equal(0, PropertyValues.GetNumber(outputs, "suspendTimeoutSeconds"));
        Assert.Equal(0.25, PropertyValues.GetNumber(outputs, "minCu"));
    }

    [Fact]
    public async Task CreatePatchesOnlyTheGivenSettingsAndWaitsForThem()
    {
        var provider = await Configured();
        _handler
            .Respond(
                HttpStatusCode.OK,
                Endpoints(
                    Endpoint("ep-other", branch: "br-dev", min: 1, max: 1, suspend: 300),
                    Endpoint("ep-1", min: 0.25, max: 0.25, suspend: 300)
                )
            )
            .Respond(HttpStatusCode.OK, Single(Endpoint("ep-1", min: 0.25, max: 0.25, suspend: 300)))
            .Respond(HttpStatusCode.OK, Single(Endpoint("ep-1", min: 0.25, max: 0.25, suspend: -1)));

        var response = await Create(provider, ("suspendTimeoutSeconds", -1));

        var patch = _handler.Requests[1];
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.Equal(
            "https://console.neon.tech/api/v2/projects/proj-1/endpoints/ep-1",
            patch.Uri.ToString()
        );
        Assert.Equal("""{"endpoint":{"suspend_timeout_seconds":-1}}""", patch.Body!.ToJsonString());
        Assert.Equal([TimeSpan.FromSeconds(2)], _delays);
        Assert.Equal(
            -1,
            PropertyValues.GetNumber(ProviderTesting.Map(response.Outputs), "suspendTimeoutSeconds")
        );
    }

    [Fact]
    public async Task CreateRejectsABranchWithoutExactlyOneReadWriteCompute()
    {
        var provider = await Configured();
        _handler.Respond(
            HttpStatusCode.OK,
            Endpoints(Endpoint("ep-ro", type: "read_only", min: 1, max: 1, suspend: 300))
        );

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(provider, ("minCu", 0.5))
        );
        Assert.Contains("found 0", error.Message);
        Assert.DoesNotContain(_handler.Requests, r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task CreateRefusesLimitsThatWouldInvertTheLiveRange()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, Endpoints(Endpoint("ep-1", min: 0.25, max: 1, suspend: 300)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(provider, ("minCu", 2.0)));
        Assert.DoesNotContain(_handler.Requests, r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task ReadReportsDriftOnlyForManagedSettings()
    {
        var provider = await Configured();
        _handler.Respond(HttpStatusCode.OK, Single(Endpoint("ep-1", min: 0.5, max: 2, suspend: 300)));

        var inputs = Props.Of(
            ("projectId", "proj-1"),
            ("branchId", "br-main"),
            ("maxCu", 1.0)
        );
        var response = await provider.Read(
            new ReadRequest(Props.Urn(Settings), "ep-1", Props.Of(), inputs),
            default
        );

        var readInputs = ProviderTesting.Map(response.Inputs);
        Assert.Equal(2, PropertyValues.GetNumber(readInputs, "maxCu"));
        Assert.False(readInputs.ContainsKey("minCu"));
    }

    [Fact]
    public async Task DeleteLeavesTheComputeAlone()
    {
        var provider = await Configured();

        await provider.Delete(
            new DeleteRequest(
                Props.Urn(Settings),
                "ep-1",
                Props.Of(("projectId", "proj-1"), ("branchId", "br-main"), ("minCu", 0.25)),
                TimeSpan.Zero,
                Props.Of()
            ),
            default
        );

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task CheckRejectsInvalidSettings()
    {
        var provider = await Configured();

        var response = await provider.Check(
            new CheckRequest(
                Props.Urn(Settings),
                Props.Of(),
                Props.Of(
                    ("projectId", "proj-1"),
                    ("branchId", "br-main"),
                    ("minCu", 2.0),
                    ("maxCu", 1.0),
                    ("suspendTimeoutSeconds", -5)
                ),
                []
            ),
            default
        );

        Assert.Equal(
            ["minCu", "suspendTimeoutSeconds"],
            response.Failures!.Select(f => f.Property).Order()
        );
    }

    private Task<CreateResponse> Create(
        NeonResourceProvider provider,
        params (string Key, object? Value)[] settings
    ) =>
        provider.Create(
            new CreateRequest(
                Props.Urn(Settings),
                Props.Of([("projectId", "proj-1"), ("branchId", "br-main"), .. settings]),
                TimeSpan.Zero,
                false
            ),
            default
        );

    private async Task<NeonResourceProvider> Configured()
    {
        var provider = new NeonResourceProvider(
            _handler,
            _ => null,
            ProviderTesting.RecordDelays(_delays)
        );
        await provider.Configure(ProviderTesting.Configure(("apiKey", "napi_key")), default);
        return provider;
    }

    private static JsonObject Endpoint(
        string id,
        double min,
        double max,
        int suspend,
        string branch = "br-main",
        string type = "read_write"
    ) =>
        new()
        {
            ["id"] = id,
            ["project_id"] = "proj-1",
            ["branch_id"] = branch,
            ["type"] = type,
            ["autoscaling_limit_min_cu"] = min,
            ["autoscaling_limit_max_cu"] = max,
            ["suspend_timeout_seconds"] = suspend,
        };

    private static JsonObject Endpoints(params JsonObject[] endpoints) =>
        new() { ["endpoints"] = new JsonArray([.. endpoints]) };

    private static JsonObject Single(JsonObject endpoint) => new() { ["endpoint"] = endpoint };
}
