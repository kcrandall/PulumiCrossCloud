using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.Azure;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Upstash;

namespace PulumiCrossCloud.Provider.Tests;

public sealed class ConnectionUriTests
{
    [Theory]
    [InlineData(
        "azure",
        "postgresql://app_user:p%23%25%3F%3A%40%2F@database.example.test:5432/app_db"
    )]
    [InlineData("upstash", "rediss://:p%23%25%3F%3A%40%2F@database.example.test:6379")]
    public async Task ConnectionUriEscapesPasswords(string provider, string expected)
    {
        string? connectionString = null;
        await Deployment.TestAsync(
            new DatabaseMocks(),
            new TestOptions { IsPreview = false },
            () =>
            {
                var connection = provider switch
                {
                    "azure" => new AzurePostgresDatabase(
                        "database",
                        "resource-group",
                        new PostgresOptions
                        {
                            MasterUsername = "app_user",
                            InitialDatabaseName = "app_db",
                        }
                    ).ConnectionString,
                    "upstash" => new UpstashRedisDatabase("database").ConnectionString,
                    _ => throw new ArgumentOutOfRangeException(nameof(provider)),
                };
                connection.Apply(value => connectionString = value);
            }
        );

        Assert.Equal(expected, connectionString);
        var uri = new Uri(Assert.IsType<string>(connectionString));
        Assert.Equal("database.example.test", uri.Host);
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
            state.TryAdd(
                "name",
                args.Inputs.TryGetValue("databaseName", out var databaseName)
                    ? databaseName
                    : args.Name ?? "database"
            );
            if (args.Type == "random:index/randomPassword:RandomPassword")
                state["result"] = "p#%?:@/";
            if (args.Type == "azure-native:dbforpostgresql:Server")
                state["fullyQualifiedDomainName"] = "database.example.test";
            if (args.Type == "upstash:index/redisDatabase:RedisDatabase")
            {
                state["endpoint"] = "database.example.test";
                state["port"] = 6379.0;
                state["password"] = "p#%?:@/";
                state["databaseId"] = "database-id";
            }
            return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
    }
}
