using Pulumi;
using Pulumi.DockerBuild.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Fly;

public sealed class FlyContainerRegistry : ComponentResource, IContainerRegistry
{
    private readonly string _registryUrl;

    public FlyContainerRegistry(string name, string appName)
        : base("crosscloud:fly:ContainerRegistry", name)
    {
        Name = name;
        _registryUrl = $"registry.fly.io/{appName}";
    }

    public string Name { get; }
    public Output<string> Arn => RegistryUrlOutput;
    public Resource Resource => this;
    public CloudProvider Provider => CloudProvider.Fly;
    public Output<string> RegistryUrlOutput => Output.Create(_registryUrl);

    public InputList<RegistryArgs> GetDockerBuildRegistryConfig() => [];

    public IEnumerable<IPolicyStatement> GetPolicy(params ContainerRegistryPermissions[] permissions) => [];
}
