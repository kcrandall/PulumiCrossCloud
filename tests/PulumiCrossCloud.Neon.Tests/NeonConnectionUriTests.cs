using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;

namespace PulumiCrossCloud.Neon.Tests;

public sealed class NeonConnectionUriTests
{
    [Fact]
    public async Task ConnectionUriEscapesProviderGeneratedPasswords()
    {
        string? connectionString = null;
        await Deployment.TestAsync(
            new DatabaseMocks(),
            new TestOptions { IsPreview = false },
            () =>
            {
                var database = new NeonPostgresDatabase(
                    "database",
                    new NeonPostgresOptions
                    {
                        MasterUsername = "app_user",
                        InitialDatabaseName = "app_db",
                    }
                );
                database.ConnectionString.Apply(value => connectionString = value);
            }
        );

        Assert.Equal(
            "postgresql://app_user:p%23%25%3F%3A%40%2F@database.neon.tech:5432/app_db",
            connectionString
        );
        var uri = new Uri(Assert.IsType<string>(connectionString));
        Assert.Equal("database.neon.tech", uri.Host);
        Assert.Equal("", uri.Fragment);
        Assert.Equal("", uri.Query);
        var password = uri.UserInfo[(uri.UserInfo.IndexOf(':') + 1)..];
        Assert.Equal("p#%?:@/", Uri.UnescapeDataString(password));
    }

    private sealed class DatabaseMocks : IMocks
    {
        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            state.TryAdd("defaultBranchId", "main-branch");
            state.TryAdd("databaseHost", "database.neon.tech");
            state.TryAdd("password", "p#%?:@/");
            return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
    }
}
