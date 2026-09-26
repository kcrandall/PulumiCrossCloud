using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace PulumiCrossCloud.Integration.Tests.Infrastructure;

/// <summary>
/// A throwaway Floci emulator container (https://floci.io) for one cloud.
/// Images are pinned so a Floci release cannot silently change what the tests exercise;
/// override a tag with FLOCI_AWS_IMAGE, FLOCI_GCP_IMAGE or FLOCI_AZURE_IMAGE.
/// </summary>
public sealed class FlociEmulator : IAsyncDisposable
{
    // The label the Testcontainers reaper (Ryuk) matches when it removes a session's resources.
    private const string ResourceReaperSessionLabel = "org.testcontainers.resource-reaper-session";

    private readonly IContainer _container;
    private readonly INetwork _network;
    private readonly ushort _port;

    private FlociEmulator(IContainer container, INetwork network, ushort port)
    {
        _container = container;
        _network = network;
        _port = port;
    }

    /// <summary>
    /// Base URL of the emulator as seen from the test process, e.g. http://127.0.0.1:49153.
    /// </summary>
    public string Endpoint => $"http://{Authority}";

    /// <summary>
    /// host:port of the emulator, for clients that take an authority rather than a URL.
    /// </summary>
    public string Authority => $"{Host}:{_container.GetMappedPublicPort(_port)}";

    private string Host => _container.Hostname == "localhost" ? "127.0.0.1" : _container.Hostname;

    /// <summary>
    /// Floci backs ECR with a registry container it starts through the Docker socket and reaches
    /// over the emulator's Docker network; without both, repositories can never be deleted.
    /// </summary>
    public static Task<FlociEmulator> StartAwsAsync() =>
        StartAsync(
            Image("FLOCI_AWS_IMAGE", "floci/floci:2.1.0"),
            4566,
            network => new Dictionary<string, string>
            {
                ["FLOCI_SERVICES_DOCKER_NETWORK"] = network,
                // Namespace and label every sidecar so parallel runs cannot collide and the
                // Testcontainers reaper removes them even when a run is killed.
                ["FLOCI_DOCKER_RESOURCE_NAMESPACE"] = network,
                ["FLOCI_DOCKER_EXTRA_LABELS_0__KEY"] = ResourceReaperSessionLabel,
                ["FLOCI_DOCKER_EXTRA_LABELS_0__VALUE"] = ResourceReaper.DefaultSessionId.ToString(
                    "D"
                ),
                ["FLOCI_SERVICES_ECR_KEEP_RUNNING_ON_SHUTDOWN"] = "false",
            },
            mountDockerSocket: true
        );

    public static Task<FlociEmulator> StartGcpAsync() =>
        StartAsync(Image("FLOCI_GCP_IMAGE", "floci/floci-gcp:0.9.0"), 4588, _ => []);

    /// <summary>
    /// floci-az only answers the Azure metadata discovery the azure-native provider performs over
    /// HTTPS, so TLS is always enabled; the same port keeps serving plain HTTP.
    /// </summary>
    public static Task<FlociEmulator> StartAzureAsync() =>
        StartAsync(
            Image("FLOCI_AZURE_IMAGE", "floci/floci-az:0.13.0"),
            4577,
            _ => new Dictionary<string, string> { ["FLOCI_AZ_TLS_ENABLED"] = "true" }
        );

    private static async Task<FlociEmulator> StartAsync(
        string image,
        ushort port,
        Func<string, Dictionary<string, string>> environment,
        bool mountDockerSocket = false
    )
    {
        var networkName = $"floci-it-{Guid.NewGuid():N}"[..20];
        var network = new NetworkBuilder().WithName(networkName).Build();
        await network.CreateAsync();

        var builder = new ContainerBuilder(image)
            .WithNetwork(network)
            .WithPortBinding(port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilContainerIsHealthy());

        if (mountDockerSocket)
            builder = builder.WithBindMount("/var/run/docker.sock", "/var/run/docker.sock");

        foreach (var (key, value) in environment(networkName))
            builder = builder.WithEnvironment(key, value);

        var container = builder.Build();
        await container.StartAsync();
        return new FlociEmulator(container, network, port);
    }

    private static string Image(string overrideVariable, string pinned) =>
        Environment.GetEnvironmentVariable(overrideVariable) is { Length: > 0 } image
            ? image
            : pinned;

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        await _network.DisposeAsync();
    }
}
