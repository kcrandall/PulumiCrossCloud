using System.Text;
using System.Text.Json;
using Pulumi;
using Pulumi.DockerBuild.Inputs;
using PulumiCrossCloud.Core;
using DO = global::Pulumi.DigitalOcean;

namespace PulumiCrossCloud.DigitalOcean;

public sealed class DigitalOceanContainerRegistry : IContainerRegistry
{
    private readonly DO.ContainerRegistry _registry;
    private readonly DO.ContainerRegistryDockerCredentials _dockerCredentials;

    public DigitalOceanContainerRegistry(
        string name,
        string region = "nyc3",
        string subscriptionTierSlug = "starter",
        DO.Provider? provider = null
    )
    {
        Name = name;
        _registry = new DO.ContainerRegistry(
            name,
            new DO.ContainerRegistryArgs
            {
                Name = name,
                Region = region,
                SubscriptionTierSlug = subscriptionTierSlug,
            },
            new CustomResourceOptions { Provider = provider }
        );

        _dockerCredentials = new DO.ContainerRegistryDockerCredentials(
            $"{name}-docker-credentials",
            new DO.ContainerRegistryDockerCredentialsArgs
            {
                RegistryName = _registry.Name,
                Write = true,
            },
            new CustomResourceOptions { Provider = provider, DependsOn = [_registry] }
        );
    }

    public string Name { get; }
    public Output<string> Arn => _registry.Id;
    public Resource Resource => _registry;
    public CloudProvider Provider => CloudProvider.DigitalOcean;
    public Output<string> RegistryUrlOutput => _registry.Endpoint;

    public InputList<RegistryArgs> GetDockerBuildRegistryConfig() =>
        [
            new RegistryArgs
            {
                Address = _registry.ServerUrl,
                Username = _dockerCredentials.DockerCredentials.Apply(credentials =>
                    ParseDockerCredentials(credentials).Username
                ),
                Password = _dockerCredentials.DockerCredentials.Apply(credentials =>
                    ParseDockerCredentials(credentials).Password
                ),
            },
        ];

    public IEnumerable<IPolicyStatement> GetPolicy(
        params ContainerRegistryPermissions[] permissions
    ) => [];

    private static DockerCredential ParseDockerCredentials(string dockerCredentials)
    {
        using var document = JsonDocument.Parse(dockerCredentials);
        if (!document.RootElement.TryGetProperty("auths", out var auths))
            throw new InvalidOperationException(
                "DigitalOcean Docker credentials did not include auths."
            );

        foreach (var auth in auths.EnumerateObject())
        {
            var value = auth.Value;
            if (
                value.TryGetProperty("username", out var username)
                && value.TryGetProperty("password", out var password)
            )
                return new DockerCredential(username.GetString() ?? "", password.GetString() ?? "");

            if (value.TryGetProperty("auth", out var encodedAuth))
            {
                var decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(encodedAuth.GetString() ?? "")
                );
                var separator = decoded.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0)
                    return new DockerCredential(decoded[..separator], decoded[(separator + 1)..]);
            }
        }

        throw new InvalidOperationException(
            "DigitalOcean Docker credentials did not include username/password data."
        );
    }

    private sealed record DockerCredential(string Username, string Password);
}
