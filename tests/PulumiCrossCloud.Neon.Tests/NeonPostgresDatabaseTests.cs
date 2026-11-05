using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.Neon;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PulumiCrossCloud.Neon.Tests;

public sealed class NeonPostgresDatabaseTests(NeonDeployment deployment)
    : IClassFixture<NeonDeployment>
{
    [Fact]
    public void ExplicitSettings_ManageTheImplicitEndpointInPlace()
    {
        var settings = deployment.Resources.Single(r => r.Name == "prod-db-compute-settings");
        Assert.Equal(NeonEndpointSettings.TypeToken, settings.Type);
        Assert.Equal("prod-db-project-id", settings.Inputs["projectId"]);
        Assert.Equal("main-branch", settings.Inputs["branchId"]);
        Assert.Equal(0.25, settings.Inputs["minCu"]);
        Assert.Equal(1.0, settings.Inputs["maxCu"]);
        Assert.Equal(-1, Convert.ToInt32(settings.Inputs["suspendTimeoutSeconds"]));
        Assert.DoesNotContain(deployment.Resources, r => r.Type == "neon:index/endpoint:Endpoint");
        Assert.DoesNotContain(deployment.Resources, r => r.Type?.StartsWith("command:") == true);

        var provider = deployment.Resources.Single(r =>
            r.Name == "prod-db-compute-settings-provider"
        );
        Assert.Equal("pulumi:providers:crosscloud-neon", provider.Type);
        Assert.Equal("mock-api-key", provider.Inputs["apiKey"]);
    }

    [Fact]
    public void UnspecifiedSettings_DoNotChangeExistingComputeBehavior()
    {
        Assert.DoesNotContain(deployment.Resources, r => r.Name == "default-db-compute-settings");
    }

    [Fact]
    public void PartialSettings_LeaveOtherSettingsUnspecified()
    {
        var settings = deployment.Resources.Single(r => r.Name == "partial-db-compute-settings");
        Assert.False(settings.Inputs.ContainsKey("minCu"));
        Assert.False(settings.Inputs.ContainsKey("maxCu"));
        Assert.Equal(0, Convert.ToInt32(settings.Inputs["suspendTimeoutSeconds"]));
    }

    [Fact]
    public void ComputeSettingsKeepsThePreviousJsonFormat()
    {
        Assert.Equal(
            "{\"autoscaling_limit_max_cu\": 1.0, \"autoscaling_limit_min_cu\": 0.25, \"endpointId\": \"ep-1\", \"suspend_timeout_seconds\": -1}",
            deployment.ProdComputeSettings
        );
        Assert.Equal(
            "{\"endpointId\": \"ep-1\", \"suspend_timeout_seconds\": 300}",
            deployment.PartialComputeSettings
        );
    }
}

public sealed class NeonDeployment : IMocks, IAsyncLifetime
{
    private Task<string>? _prod;
    private Task<string>? _partial;

    public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();
    public string? ProdComputeSettings { get; private set; }
    public string? PartialComputeSettings { get; private set; }

    public async Task InitializeAsync()
    {
        await Deployment.TestAsync(
            this,
            new TestOptions { IsPreview = false },
            () =>
            {
                var factory = new NeonCloudResourceFactory("mock-api-key");
                var prod = (NeonPostgresDatabase)
                    factory.CreatePostgresDatabase(
                        "prod-db",
                        new NeonPostgresOptions
                        {
                            MinCu = 0.25,
                            MaxCu = 1,
                            SuspendTimeoutSeconds = -1,
                        }
                    );
                factory.CreatePostgresDatabase("default-db");
                var partial = (NeonPostgresDatabase)
                    factory.CreatePostgresDatabase(
                        "partial-db",
                        new NeonPostgresOptions { SuspendTimeoutSeconds = 0 }
                    );
                _prod = prod.ComputeSettings!.GetValueAsync();
                _partial = partial.ComputeSettings!.GetValueAsync();
            }
        );
        ProdComputeSettings = await _prod!;
        PartialComputeSettings = await _partial!;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
    {
        Resources.Enqueue(args);
        var state = args.Inputs.ToDictionary(p => p.Key, p => p.Value);
        state.TryAdd("defaultBranchId", "main-branch");
        state.TryAdd("databaseHost", "test.neon.tech");
        state.TryAdd("password", "mock-password");
        state.TryAdd("endpointId", "ep-1");
        return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
    }

    public Task<object> CallAsync(MockCallArgs args) =>
        Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
}

internal static class OutputTestExtensions
{
    public static Task<T> GetValueAsync<T>(this Output<T> output)
    {
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        output.Apply(value =>
        {
            completion.TrySetResult(value);
            return value;
        });
        return completion.Task;
    }
}
