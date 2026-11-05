using System.Net;
using System.Text.Json.Nodes;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.Koyeb;

namespace PulumiCrossCloud.Plugin.Tests;

public sealed class KoyebResourceProviderTests
{
    private const string Sleep = ServiceSleepResource.Type;

    private readonly FakeApiHandler _handler = new();
    private readonly List<TimeSpan> _delays = [];

    [Fact]
    public async Task CreateAddsTheSleepTargetAndKeepsTheRestOfTheDefinition()
    {
        var provider = await Configured();
        _handler
            .Respond(HttpStatusCode.OK, Service("dep_1"))
            .Respond(HttpStatusCode.OK, Deployment("dep_1", "HEALTHY", Definition()))
            .Respond(HttpStatusCode.OK, Service("dep_2"))
            .Respond(HttpStatusCode.OK, Deployment("dep_2", "STARTING", Definition()))
            .Respond(HttpStatusCode.OK, Deployment("dep_2", "HEALTHY", Definition()));

        var response = await provider.Create(
            new CreateRequest(
                Props.Urn(Sleep),
                Props.Of(("serviceId", "svc_1"), ("lightSleepDelaySeconds", 60), ("deepSleepDelaySeconds", 1800)),
                TimeSpan.Zero,
                false
            ),
            default
        );

        Assert.Equal("svc_1", response.Id);
        var patch = _handler.Requests[2];
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.Equal("https://app.koyeb.com/v1/services/svc_1", patch.Uri.ToString());
        Assert.Equal("Bearer koyeb_token", patch.Authorization);
        Assert.True(patch.Body!["skip_build"]!.GetValue<bool>());
        var definition = patch.Body!["definition"]!;
        Assert.Equal("registry/image:1", definition["docker"]!["image"]!.GetValue<string>());
        var targets = definition["scalings"]![0]!["targets"]!.AsArray();
        Assert.Equal(2, targets.Count);
        Assert.NotNull(targets[0]!["average_cpu"]);
        Assert.Equal(
            """{"light_sleep_value":60,"deep_sleep_value":1800}""",
            targets[1]!["sleep_idle_delay"]!.ToJsonString()
        );
        Assert.Equal([TimeSpan.FromSeconds(10)], _delays);
        Assert.Equal(
            "dep_2",
            PropertyValues.GetString(ProviderTesting.Map(response.Outputs), "deploymentId")
        );
    }

    [Fact]
    public async Task CreateSkipsTheRedeployWhenTheDelaysAlreadyMatch()
    {
        var provider = await Configured();
        _handler
            .Respond(HttpStatusCode.OK, Service("dep_1"))
            .Respond(HttpStatusCode.OK, Deployment("dep_1", "SLEEPING", Definition(light: "60", deep: 1800)));

        await provider.Create(
            new CreateRequest(
                Props.Urn(Sleep),
                Props.Of(("serviceId", "svc_1"), ("lightSleepDelaySeconds", 60), ("deepSleepDelaySeconds", 1800)),
                TimeSpan.Zero,
                false
            ),
            default
        );

        Assert.Equal(2, _handler.Requests.Count);
        Assert.DoesNotContain(_handler.Requests, r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task AFailedDeploymentFailsTheUpdate()
    {
        var provider = await Configured();
        _handler
            .Respond(HttpStatusCode.OK, Service("dep_1"))
            .Respond(HttpStatusCode.OK, Deployment("dep_1", "HEALTHY", Definition()))
            .Respond(HttpStatusCode.OK, Service("dep_2"))
            .Respond(HttpStatusCode.OK, Deployment("dep_2", "ERROR", Definition()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.Update(
                new UpdateRequest(
                    Props.Urn(Sleep),
                    "svc_1",
                    Props.Of(),
                    Props.Of(("serviceId", "svc_1"), ("deepSleepDelaySeconds", 600)),
                    TimeSpan.Zero,
                    [],
                    false,
                    Props.Of()
                ),
                default
            )
        );

        Assert.Contains("dep_2", error.Message);
        Assert.Contains("ERROR", error.Message);
    }

    [Fact]
    public async Task ReadReportsTheLiveDelays()
    {
        var provider = await Configured();
        _handler
            .Respond(HttpStatusCode.OK, Service("dep_1"))
            .Respond(HttpStatusCode.OK, Deployment("dep_1", "HEALTHY", Definition(deep: 300)));

        var response = await provider.Read(
            new ReadRequest(
                Props.Urn(Sleep),
                "svc_1",
                Props.Of(),
                Props.Of(("serviceId", "svc_1"), ("deepSleepDelaySeconds", 1800))
            ),
            default
        );

        var inputs = ProviderTesting.Map(response.Inputs);
        Assert.Equal(300, PropertyValues.GetNumber(inputs, "deepSleepDelaySeconds"));
        Assert.False(inputs.ContainsKey("lightSleepDelaySeconds"));
    }

    [Fact]
    public async Task TriggersReapplyInsteadOfReplacing()
    {
        var provider = await Configured();
        var olds = Props.Of(("serviceId", "svc_1"), ("deepSleepDelaySeconds", 600), ("triggers", new[] { "3" }));
        var news = Props.Of(("serviceId", "svc_1"), ("deepSleepDelaySeconds", 600), ("triggers", new[] { "4" }));

        var diff = await provider.Diff(new DiffRequest(Props.Urn(Sleep), "svc_1", olds, news, [], olds), default);

        Assert.Equal(["triggers"], diff.Diffs);
        Assert.Empty(diff.Replaces!);
    }

    private async Task<KoyebResourceProvider> Configured()
    {
        var provider = new KoyebResourceProvider(_handler, _ => null, ProviderTesting.RecordDelays(_delays));
        await provider.Configure(ProviderTesting.Configure(("apiToken", "koyeb_token")), default);
        return provider;
    }

    private static JsonObject Service(string latest) =>
        new()
        {
            ["service"] = new JsonObject
            {
                ["id"] = "svc_1",
                ["name"] = "api",
                ["latest_deployment_id"] = latest,
            },
        };

    private static JsonObject Deployment(string id, string status, JsonObject definition) =>
        new()
        {
            ["deployment"] = new JsonObject
            {
                ["id"] = id,
                ["status"] = status,
                ["definition"] = definition,
            },
        };

    private static JsonObject Definition(string? light = null, long? deep = null)
    {
        var targets = new JsonArray(new JsonObject { ["average_cpu"] = new JsonObject { ["value"] = 80 } });
        if (light is not null || deep is not null)
        {
            var sleep = new JsonObject();
            if (light is not null)
                sleep["light_sleep_value"] = light; // Koyeb encodes int64 as a string here.
            if (deep is not null)
                sleep["deep_sleep_value"] = deep;
            targets.Add(new JsonObject { ["sleep_idle_delay"] = sleep });
        }

        return new JsonObject
        {
            ["name"] = "api",
            ["type"] = "WEB",
            ["docker"] = new JsonObject { ["image"] = "registry/image:1" },
            ["scalings"] = new JsonArray(
                new JsonObject { ["min"] = 0, ["max"] = 1, ["targets"] = targets }
            ),
        };
    }
}
