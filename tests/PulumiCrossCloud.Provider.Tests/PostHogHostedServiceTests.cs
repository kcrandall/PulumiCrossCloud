using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.Fly;
using PulumiCrossCloud.PostHog;

namespace PulumiCrossCloud.Provider.Tests;

public sealed class PostHogHostedServiceTests(PostHogHostingDeployment deployment)
    : IClassFixture<PostHogHostingDeployment>
{
    [Theory]
    [InlineData("reserved", "p@ss:/?#%+ space", "p%40ss%3A%2F%3F%23%25%2B%20space")]
    [InlineData("simple", "plain-password", "plain-password")]
    public void RedisPassword_PreservesCredentialsWithoutChangingTheEndpoint(
        string name,
        string password,
        string encodedPassword
    )
    {
        var url = deployment.RedisUrl(name);
        Assert.Equal($"redis://:{encodedPassword}@cache.example.com:6380/2", url);
        var parsed = new Uri(url);
        Assert.Equal("cache.example.com", parsed.Host);
        Assert.Equal(6380, parsed.Port);
        Assert.Equal("/2", parsed.AbsolutePath);
        Assert.Equal(password, Uri.UnescapeDataString(parsed.UserInfo[1..]));
    }

    [Fact]
    public void MissingRedisPassword_ProducesAnUnauthenticatedUrl() =>
        Assert.Equal("redis://cache.example.com:6380/2", deployment.RedisUrl("no-password"));

    [Fact]
    public void ExplicitRedisUrl_IsForwardedWithoutReencoding() =>
        Assert.Equal(
            "rediss://:already%40encoded@custom.example.com:6379/4",
            deployment.RedisUrl("explicit-url")
        );
}

public sealed class PostHogHostingDeployment : IMocks, IAsyncLifetime
{
    private readonly ConcurrentQueue<MockResourceArgs> _resources = new();

    public async Task InitializeAsync() =>
        await Deployment.TestAsync<PostHogHostingStack>(
            this,
            new TestOptions { IsPreview = false }
        );

    public Task DisposeAsync() => Task.CompletedTask;

    public string RedisUrl(string name)
    {
        var machine = _resources.Single(resource => resource.Name == $"{name}-machine");
        var env = Assert.IsType<ImmutableDictionary<string, object>>(machine.Inputs["env"]);
        return Assert.IsType<string>(env["REDIS_URL"]);
    }

    public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
    {
        _resources.Enqueue(args);
        var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
        state.TryAdd("appUrl", $"https://{args.Name}.fly.dev");
        return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
    }

    public Task<object> CallAsync(MockCallArgs args) =>
        Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
}

public sealed class PostHogHostingStack : Stack
{
    public PostHogHostingStack()
    {
        var factory = new FlyCloudResourceFactory();
        (string Name, string? Password)[] cases =
        [
            ("reserved", "p@ss:/?#%+ space"),
            ("simple", "plain-password"),
            ("no-password", null),
        ];
        foreach (var (name, password) in cases)
        {
            _ = new PostHogHostedService(
                factory,
                name,
                new PostHogHostingArgs
                {
                    SecretKey = new FlySecret($"{name}-key", name, "test-secret-key"),
                    SiteUrl = $"https://{name}.example.com",
                    MirrorImageToRegistry = false,
                    Redis = new PostHogRedisInputs
                    {
                        Host = "cache.example.com",
                        Port = 6380,
                        Database = 2,
                        Password = password is null
                            ? null
                            : new FlySecret($"{name}-password", name, password),
                    },
                }
            );
        }

        _ = new PostHogHostedService(
            factory,
            "explicit-url",
            new PostHogHostingArgs
            {
                SecretKey = new FlySecret("explicit-key", "explicit-url", "test-secret-key"),
                SiteUrl = "https://explicit.example.com",
                MirrorImageToRegistry = false,
                Redis = new PostHogRedisInputs
                {
                    Url = new FlySecret(
                        "explicit-redis-url",
                        "explicit-url",
                        "rediss://:already%40encoded@custom.example.com:6379/4"
                    ),
                },
            }
        );
    }
}
