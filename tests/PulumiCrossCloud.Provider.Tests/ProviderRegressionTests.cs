using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi;
using Pulumi.DockerBuild.Inputs;
using Pulumi.Testing;
using PulumiCrossCloud.Azure;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.DigitalOcean;
using PulumiCrossCloud.Fly;
using PulumiCrossCloud.Koyeb;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PulumiCrossCloud.Provider.Tests;

public sealed class ProviderRegressionTests(ProviderDeployment deployment)
    : IClassFixture<ProviderDeployment>
{
    [Fact]
    public void FlyMachine_InjectsSecretValuesIntoEachEnvironmentMapping()
    {
        var machine = deployment.Resources.Single(resource => resource.Name == "test-fly-machine");
        var env = Assert.IsType<ImmutableDictionary<string, object>>(machine.Inputs["env"]);
        Assert.Equal("actual-secret-value", env["FIRST_PASSWORD"]);
        Assert.Equal("actual-secret-value", env["SECOND_PASSWORD"]);
    }

    [Theory]
    [InlineData("fly", "test-fly.fly.dev")]
    [InlineData("koyeb", "test-koyeb.koyeb.app")]
    public void ComputeCname_ContainsOnlyTheServiceHostname(string provider, string expected)
    {
        var record = deployment.Resources.Single(resource => resource.Name == $"{provider}-cname");
        Assert.Equal(expected, record.Inputs["value"]);
    }

    [Fact]
    public void AzureBucket_AcceptsHyphenatedNamesAndSanitizesOnlyTheStorageAccountName()
    {
        var account = deployment.Resources.Single(resource => resource.Name == "myassets");
        var container = deployment.Resources.Single(resource => resource.Name == "my-assets");
        Assert.Equal("myassets", account.Inputs["accountName"]);
        Assert.Equal("my-assets", container.Inputs["containerName"]);
    }

    [Theory]
    [InlineData("fly")]
    [InlineData("koyeb")]
    [InlineData("digitalocean")]
    public void RepeatedSecrets_AreAllowedWithDifferentEnvironmentMappings(string provider)
    {
        var configuration = ProviderRegressionStack.Configurations[provider];
        Assert.Single(configuration.SecretMappings);
        Assert.Equal("SECOND_PASSWORD", configuration.SecretMappings["password-secret"]);
    }
}

public sealed class ProviderDeployment : IMocks, IAsyncLifetime
{
    public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

    public async Task InitializeAsync() =>
        await Deployment.TestAsync<ProviderRegressionStack>(
            this,
            new TestOptions { IsPreview = false }
        );

    public Task DisposeAsync() => Task.CompletedTask;

    public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
    {
        Resources.Enqueue(args);
        var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
        state.TryAdd("name", args.Name ?? "resource");
        if (args.Type == "fly:index/app:App")
            state["appUrl"] = "https://test-fly.fly.dev/path";
        if (args.Type == "koyeb:index/app:App")
            state["domains"] = new[]
            {
                new Dictionary<string, object> { ["name"] = "test-koyeb.koyeb.app" },
            };
        return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
    }

    public Task<object> CallAsync(MockCallArgs args) =>
        Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
}

public sealed class ProviderRegressionStack : Stack
{
    public static Dictionary<string, ComputeConfigurationResponse> Configurations { get; } = [];

    public ProviderRegressionStack()
    {
        var secret = new FlySecret("password-secret", "test-fly", "actual-secret-value");
        var flyRegistry = new FlyContainerRegistry("fly-registry", "test-fly");
        var fly = new FlyMachineCompute("test-fly", flyRegistry, "image");
        ConfigureRepeatedSecret("fly", fly, secret);
        fly.Build();
        var koyeb = new KoyebServiceCompute(
            "test-koyeb",
            new KoyebContainerRegistry("koyeb-registry"),
            "image"
        );
        ConfigureRepeatedSecret("koyeb", koyeb, secret);
        koyeb.Build();
        var digitalOcean = new DigitalOceanAppPlatformCompute(
            "test-digitalocean",
            new TestRegistry("do-registry", CloudProvider.DigitalOcean),
            "image"
        );
        ConfigureRepeatedSecret("digitalocean", digitalOcean, secret);

        new TestDnsRecord(
            "fly-cname",
            fly.GetRequiredDnsRecords(["app.example.com"]).Single().Value
        );
        new TestDnsRecord(
            "koyeb-cname",
            koyeb.GetRequiredDnsRecords(["app.example.com"]).Single().Value
        );
        _ = new AzureBlobBucket("my-assets", "test-resource-group");
    }

    private static void ConfigureRepeatedSecret(string provider, ICompute compute, ISecret secret)
    {
        compute.AddRequiredSecret(secret, "FIRST_PASSWORD");
        compute.AddRequiredSecret(secret, "SECOND_PASSWORD");
        Configurations[provider] = compute.GetConfiguration();
    }

    private sealed class TestRegistry : ComponentResource, IContainerRegistry
    {
        public TestRegistry(string name, CloudProvider provider)
            : base("test:index:Registry", name)
        {
            Name = name;
            Provider = provider;
        }

        public string Name { get; }
        public Output<string> Arn => Output.Create(Name);
        public Resource Resource => this;
        public CloudProvider Provider { get; }
        public Output<string> RegistryUrlOutput => Output.Create("registry.example.com");

        public InputList<RegistryArgs> GetDockerBuildRegistryConfig() => [];

        public IEnumerable<IPolicyStatement> GetPolicy(
            params ContainerRegistryPermissions[] permissions
        ) => [];
    }

    private sealed class TestDnsRecord(string name, Output<string> value)
        : CustomResource(
            "test:index:DnsRecord",
            name,
            new TestDnsRecordArgs { Value = value },
            new CustomResourceOptions(),
            registerPackageRequest: null
        );

    private sealed class TestDnsRecordArgs : ResourceArgs
    {
        [Input("value")]
        public Input<string> Value { get; set; } = null!;
    }
}
