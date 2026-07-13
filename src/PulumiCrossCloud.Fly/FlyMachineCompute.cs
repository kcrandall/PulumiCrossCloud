using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Fly;

public sealed class FlyMachineCompute : ICompute
{
    private readonly List<(ISecret secret, string? envVarName)> _requiredSecrets = [];
    private readonly Dictionary<string, string> _environmentVariables = new();
    private readonly Dictionary<string, Output<string>> _environmentVariablesOutput = new();
    private readonly List<IPolicyStatement> _customPolicyStatements = [];
    private readonly List<Func<ICompute, Task>> _onHealthyActions = [];
    private readonly List<Func<ICompute, Task>> _onReadyActions = [];
    private readonly string _imageName;
    private readonly string _imageTag;
    private readonly string _orgSlug;
    private readonly string _region;
    private readonly global::Pulumi.Fly.Provider? _provider;
    private global::Pulumi.Fly.App? _app;
    private global::Pulumi.Fly.Machine? _machine;

    public FlyMachineCompute(
        string name,
        IContainerRegistry containerRegistry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        string orgSlug = "personal",
        string region = "iad",
        global::Pulumi.Fly.Provider? provider = null
    )
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        _imageName =
            imageName
            ?? containerImage?.Name
            ?? throw new ArgumentException("Either imageName or containerImage must be provided.");
        _imageTag = imageTag;
        _orgSlug = orgSlug;
        _region = region;
        _provider = provider;
    }

    public string Name { get; }
    public ComputeServiceType ServiceType => ComputeServiceType.FlyMachine;
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets.Select(x => x.secret);
    public IContainerRegistry ContainerRegistry { get; }
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
    public Output<string> ServiceUrl => _app?.AppUrl ?? Output.Create("");
    public Output<string> Arn => _machine?.Id ?? Output.Create("");
    public CloudProvider Provider => CloudProvider.Fly;
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
    public IList<PortForwardingConfiguration> PortForwardings { get; set; } =
    [
        new PortForwardingConfiguration
        {
            ContainerPort = 8080,
            ExternalPort = 443,
            Protocol = "tcp",
        },
    ];
    public string? MemoryLimit { get; set; }
    public string? CpuLimit { get; set; }

    public void AddRequiredSecret(ISecret secret, string? environmentVariableName = null) =>
        _requiredSecrets.Add((secret, environmentVariableName));

    public void AddEnvironmentVariable(string name, string value) =>
        _environmentVariables[name] = value;

    public void AddEnvironmentVariable(string name, Output<string> value) =>
        _environmentVariablesOutput[name] = value;

    public void AddEnvironmentVariables(IDictionary<string, string> variables)
    {
        foreach (var variable in variables)
            _environmentVariables[variable.Key] = variable.Value;
    }

    public void AddPolicyStatement(IPolicyStatement statement) =>
        _customPolicyStatements.Add(statement);

    public void Build()
    {
        var options = new CustomResourceOptions { Provider = _provider };
        _app = new global::Pulumi.Fly.App(
            $"{Name}-app",
            new global::Pulumi.Fly.AppArgs { Name = Name, OrgSlug = _orgSlug },
            options
        );

        _machine = new global::Pulumi.Fly.Machine(
            $"{Name}-machine",
            BuildMachineArgs(_app.Name),
            new CustomResourceOptions { Provider = _provider, DependsOn = [_app] }
        );
    }

    public IEnumerable<Resource> GetDependencies() => _machine is null ? [] : [_machine];

    public IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names) =>
        names.Select(name => new DnsRecord(DnsRecordType.CNAME, name, ServiceUrl, $"{name}-cname"));

    public IEnumerable<DnsRecord> RegisterDnsMappingAndCertificates(
        IEnumerable<string> customDomains
    ) => GetRequiredDnsRecords(customDomains);

    public ComputeConfigurationResponse GetConfiguration() =>
        new()
        {
            EnvironmentVariables = new Dictionary<string, string>(_environmentVariables),
            EnvironmentVariablesOutput = new Dictionary<string, Output<string>>(
                _environmentVariablesOutput
            ),
            RequiredSecrets = _requiredSecrets.Select(x => x.secret).ToList(),
            SecretMappings = _requiredSecrets.ToDictionary(x => x.secret.Name, x => x.envVarName),
        };

    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions) => [];

    public void OnHealthy(params Func<ICompute, Task>[] actions) =>
        _onHealthyActions.AddRange(actions);

    public void OnReady(params Func<ICompute, Task>[] actions) => _onReadyActions.AddRange(actions);

    private global::Pulumi.Fly.MachineArgs BuildMachineArgs(Input<string> appName)
    {
        var args = new global::Pulumi.Fly.MachineArgs
        {
            App = appName,
            Region = _region,
            Name = Name,
            Image = BuildImageUri(),
            DesiredStatus = "started",
            Restart = new global::Pulumi.Fly.Inputs.MachineRestartArgs { Policy = "always" },
        };

        var guest = BuildGuestArgs();
        if (guest is not null)
            args.Guest = guest;

        foreach (var variable in _environmentVariables)
            args.Env.Add(variable.Key, variable.Value);

        foreach (var variable in _environmentVariablesOutput)
            args.Env.Add(variable.Key, variable.Value);

        foreach (var (secret, envVarName) in _requiredSecrets)
            args.Env.Add(envVarName ?? ToEnvironmentName(secret.Name), secret.Name);

        foreach (var forwarding in PortForwardings)
        {
            var externalPort =
                forwarding.ExternalPort
                ?? (
                    forwarding.Protocol?.Equals("http", StringComparison.OrdinalIgnoreCase) == true
                        ? 80
                        : 443
                );
            args.Services.Add(
                new global::Pulumi.Fly.Inputs.MachineServiceArgs
                {
                    InternalPort = forwarding.ContainerPort,
                    Protocol = forwarding.Protocol ?? "tcp",
                    MinMachinesRunning = MinCount,
                    Ports =
                    [
                        new global::Pulumi.Fly.Inputs.MachineServicePortArgs
                        {
                            Port = externalPort,
                            Handlers = externalPort == 443 ? ["tls", "http"] : ["http"],
                            ForceHttps = externalPort == 443,
                        },
                    ],
                }
            );
        }

        return args;
    }

    private global::Pulumi.Fly.Inputs.MachineGuestArgs? BuildGuestArgs()
    {
        var memoryMb = ParseMemoryMb(MemoryLimit);
        var cpus = ParseCpu(CpuLimit);
        if (memoryMb is null && cpus is null)
            return null;

        return new global::Pulumi.Fly.Inputs.MachineGuestArgs
        {
            MemoryMb = memoryMb,
            Cpus = cpus,
            CpuKind = "shared",
        };
    }

    private Output<string> BuildImageUri() =>
        ContainerRegistry.RegistryUrlOutput.Apply(registryUrl =>
            string.IsNullOrWhiteSpace(registryUrl)
                ? $"{_imageName}:{_imageTag}"
                : $"{registryUrl.TrimEnd('/')}/{_imageName}:{_imageTag}"
        );

    private static double? ParseMemoryMb(string? memoryLimit)
    {
        if (string.IsNullOrWhiteSpace(memoryLimit))
            return null;

        var trimmed = memoryLimit.Trim();
        if (
            trimmed.EndsWith("Gi", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(trimmed[..^2], out var gib)
        )
            return gib * 1024;

        if (
            trimmed.EndsWith("Mi", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(trimmed[..^2], out var mib)
        )
            return mib;

        return double.TryParse(trimmed, out var raw) ? raw : null;
    }

    private static double? ParseCpu(string? cpuLimit) =>
        double.TryParse(cpuLimit, out var value) ? value : null;

    private static string ToEnvironmentName(string name) =>
        name.Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
}
