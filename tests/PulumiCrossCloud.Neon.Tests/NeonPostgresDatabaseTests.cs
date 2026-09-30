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
        var command = deployment.Resources.Single(r => r.Name == "prod-db-compute-settings");
        var environment = (ImmutableDictionary<string, object>)command.Inputs["environment"];
        Assert.Equal("prod-db-project-id", environment["NEON_PROJECT_ID"]);
        Assert.Equal("main-branch", environment["NEON_BRANCH_ID"]);
        Assert.Equal("0.25", environment["NEON_MIN_CU"]);
        Assert.Equal("0.25", environment["NEON_MAX_CU"]);
        Assert.Equal("-1", environment["NEON_SUSPEND_TIMEOUT_SECONDS"]);
        Assert.DoesNotContain(deployment.Resources, r => r.Type == "neon:index/endpoint:Endpoint");
        Assert.False(command.Inputs.ContainsKey("delete"));
        Assert.Equal(command.Inputs["create"], command.Inputs["update"]);
    }

    [Fact]
    public void UnspecifiedSettings_DoNotChangeExistingComputeBehavior()
    {
        Assert.DoesNotContain(deployment.Resources, r => r.Name == "default-db-compute-settings");
    }

    [Fact]
    public void PartialSettings_LeaveOtherSettingsUnspecified()
    {
        var command = deployment.Resources.Single(r => r.Name == "partial-db-compute-settings");
        var environment = (ImmutableDictionary<string, object>)command.Inputs["environment"];
        Assert.Equal("", environment["NEON_MIN_CU"]);
        Assert.Equal("", environment["NEON_MAX_CU"]);
        Assert.Equal("300", environment["NEON_SUSPEND_TIMEOUT_SECONDS"]);
    }
}

public sealed class NeonDeployment : IMocks, IAsyncLifetime
{
    public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

    public async Task InitializeAsync() =>
        await Deployment.TestAsync<NeonStack>(this, new TestOptions { IsPreview = false });

    public Task DisposeAsync() => Task.CompletedTask;

    public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
    {
        Resources.Enqueue(args);
        var state = args.Inputs.ToDictionary(p => p.Key, p => p.Value);
        state.TryAdd("defaultBranchId", "main-branch");
        state.TryAdd("databaseHost", "test.neon.tech");
        state.TryAdd("password", "mock-password");
        state.TryAdd("stdout", "{}");
        return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
    }

    public Task<object> CallAsync(MockCallArgs args) =>
        Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
}

public sealed class NeonStack : Stack
{
    public NeonStack()
    {
        var factory = new NeonCloudResourceFactory("mock-api-key");
        factory.CreatePostgresDatabase(
            "prod-db",
            new NeonPostgresOptions
            {
                MinCu = 0.25,
                MaxCu = 0.25,
                SuspendTimeoutSeconds = -1,
            }
        );
        factory.CreatePostgresDatabase("default-db");
        factory.CreatePostgresDatabase(
            "partial-db",
            new NeonPostgresOptions { SuspendTimeoutSeconds = 300 }
        );
    }
}
