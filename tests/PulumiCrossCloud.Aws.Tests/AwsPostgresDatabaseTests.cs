using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws.Tests;

public sealed class AwsPostgresDatabaseTests
{
    [Theory]
    [InlineData(false, "db-resource-id")]
    [InlineData(true, "cluster-resource-id")]
    public async Task ConnectionUriEscapesPasswordsAndConnectPolicyUsesDatabaseUserArn(
        bool aurora,
        string resourceId
    )
    {
        string? connectionString = null;
        string? connectArn = null;
        await Deployment.TestAsync(
            new DatabaseMocks(),
            new TestOptions { IsPreview = false },
            () =>
            {
                AwsPostgresOptions options = aurora
                    ? new AwsAuroraPostgresOptions()
                    : new AwsRdsPostgresOptions();
                options.MasterUsername = "app_user";
                options.InitialDatabaseName = "app_db";
                var database = new AwsPostgresDatabase("database", options);
                database.ConnectionString.Apply(value => connectionString = value);
                Assert
                    .Single(database.GetPolicy(PostgresPermissions.Connect))
                    .Resources.Single()
                    .Apply(value => connectArn = value);
            }
        );

        Assert.Equal(
            "postgresql://app_user:p%23%25%3F%3A%40%2F@database.example.test:5432/app_db",
            connectionString
        );
        var uri = new Uri(Assert.IsType<string>(connectionString));
        Assert.Equal("", uri.Fragment);
        Assert.Equal("", uri.Query);
        Assert.Equal(
            $"arn:aws-us-gov:rds-db:us-gov-west-1:123456789012:dbuser:{resourceId}/app_user",
            connectArn
        );
    }

    private sealed class DatabaseMocks : IMocks
    {
        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            state.TryAdd("name", args.Name ?? "database");
            if (args.Type == "random:index/randomPassword:RandomPassword")
                state["result"] = "p#%?:@/";
            if (args.Type == "aws:rds/instance:Instance")
            {
                state["arn"] = "arn:aws-us-gov:rds:us-gov-west-1:123456789012:db:database";
                state["resourceId"] = "db-resource-id";
                state["address"] = "database.example.test";
            }
            if (args.Type == "aws:rds/cluster:Cluster")
            {
                state["arn"] = "arn:aws-us-gov:rds:us-gov-west-1:123456789012:cluster:database";
                state["clusterResourceId"] = "cluster-resource-id";
                state["endpoint"] = "database.example.test";
            }
            return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
    }
}
