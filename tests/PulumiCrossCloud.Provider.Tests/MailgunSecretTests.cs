using System.Collections.Concurrent;
using Pulumi;
using Pulumi.DockerBuild.Inputs;
using Pulumi.Testing;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.DigitalOcean;
using PulumiCrossCloud.Mailgun;

namespace PulumiCrossCloud.Provider.Tests;

public sealed class MailgunSecretTests
{
    [Fact]
    public async Task SmtpPasswordHasARealDependencyAndCanBeConsumedByCompute()
    {
        var mocks = new MailgunMocks();
        ISecret? password = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                var email = new MailgunEmailService("email", "mail.example.com");
                password = email.Password;
                Assert.IsType<Pulumi.Mailgun.Domain>(password.Resource);
                Assert.Same(password.Resource, password.ValueResource);

                var compute = new DigitalOceanAppPlatformCompute(
                    "consumer",
                    new TestRegistry(),
                    "image"
                );
                compute.AddRequiredSecret(password, "SMTP_PASSWORD");
                compute.Build();
            }
        );

        Assert.NotNull(password);
        Assert.True(await Output.IsSecretAsync(password.Value));
        Assert.Contains(mocks.Resources, resource => resource.Name == "consumer-app");
    }

    private sealed class TestRegistry : ComponentResource, IContainerRegistry
    {
        public TestRegistry()
            : base("test:index:Registry", "registry") { }

        public string Name => "registry";
        public Output<string> Arn => Urn;
        public CloudProvider Provider => CloudProvider.DigitalOcean;
        public Resource Resource => this;
        public Output<string> RegistryUrlOutput => Output.Create("registry.example.com");

        public InputList<RegistryArgs> GetDockerBuildRegistryConfig() => [];

        public IEnumerable<IPolicyStatement> GetPolicy(
            params ContainerRegistryPermissions[] permissions
        ) => [];
    }

    private sealed class MailgunMocks : IMocks
    {
        public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            Resources.Enqueue(args);
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (args.Type == "random:index/randomPassword:RandomPassword")
                state["result"] = "generated-smtp-password";
            if (args.Type == "mailgun:index/domain:Domain")
                state["sendingRecordsSets"] = Array.Empty<object>();
            return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(new Dictionary<string, object>());
    }
}
