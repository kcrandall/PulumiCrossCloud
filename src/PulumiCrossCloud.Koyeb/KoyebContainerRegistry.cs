using Pulumi;
using Pulumi.DockerBuild.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Koyeb;

public sealed class KoyebContainerRegistry : ComponentResource, IContainerRegistry
{
    private readonly string _registryUrl;

    public KoyebContainerRegistry(string name, string registryUrl = "")
        : base("crosscloud:koyeb:ContainerRegistry", name)
    {
        Name = name;
        _registryUrl = registryUrl;
    }

    public string Name { get; }
    public Output<string> Arn => RegistryUrlOutput;
    public Resource Resource => this;
    public CloudProvider Provider => CloudProvider.Koyeb;
    public Output<string> RegistryUrlOutput => Output.Create(_registryUrl);

    public InputList<RegistryArgs> GetDockerBuildRegistryConfig() => [];

    public IEnumerable<IPolicyStatement> GetPolicy(
        params ContainerRegistryPermissions[] permissions
    ) => [];
}
