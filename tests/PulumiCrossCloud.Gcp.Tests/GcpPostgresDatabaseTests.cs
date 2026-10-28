using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;

namespace PulumiCrossCloud.Gcp.Tests;

public sealed class GcpPostgresDatabaseTests
{
    [Fact]
    public async Task ConnectionUriEscapesGeneratedPasswords()
    {
        string? connectionString = null;
        await Deployment.TestAsync(
            new DatabaseMocks(),
            new TestOptions { IsPreview = false },
            () =>
            {
                GcpProjectConfig.ProjectId = CloudRunDeployment.ProjectId;
                var database = new GcpPostgresDatabase(
                    "database",
                    new GcpPostgresOptions
                    {
                        MasterUsername = "app_user",
                        InitialDatabaseName = "app_db",
                        PubliclyAccessible = true,
                    }
                );
                database.ConnectionString.Apply(value => connectionString = value);
            }
        );

        Assert.Equal(
            "postgresql://app_user:p%23%25%3F%3A%40%2F@192.0.2.1:5432/app_db",
            connectionString
        );
        var uri = new Uri(Assert.IsType<string>(connectionString));
        Assert.Equal("", uri.Fragment);
        Assert.Equal("", uri.Query);
    }

    private sealed class DatabaseMocks : IMocks
    {
        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            state.TryAdd("name", args.Name ?? "database");
            if (args.Type == "random:index/randomPassword:RandomPassword")
                state["result"] = "p#%?:@/";
            if (args.Type == "gcp:sql/databaseInstance:DatabaseInstance")
                state["publicIpAddress"] = "192.0.2.1";
            return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
    }
}
