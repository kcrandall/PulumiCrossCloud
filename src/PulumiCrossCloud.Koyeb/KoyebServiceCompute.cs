using System.Collections.Immutable;
using Pulumi;
using Pulumi.Command.Local;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Koyeb;

public sealed class KoyebServiceCompute : ICompute
{
    private readonly List<(ISecret secret, string? envVarName)> _requiredSecrets = [];
    private readonly Dictionary<string, string> _environmentVariables = new();
    private readonly Dictionary<string, Output<string>> _environmentVariablesOutput = new();
    private readonly List<IPolicyStatement> _customPolicyStatements = [];
    private readonly List<Func<ICompute, Task>> _onHealthyActions = [];
    private readonly List<Func<ICompute, Task>> _onReadyActions = [];
    private readonly string _imageName;
    private readonly string _imageTag;
    private readonly string _region;
    private readonly string _instanceType;
    private readonly global::Pulumi.Koyeb.Provider? _provider;
    private global::Pulumi.Koyeb.App? _app;
    private global::Pulumi.Koyeb.Service? _service;
    private Command? _scaleToZeroSleepCommand;

    public KoyebServiceCompute(
        string name,
        IContainerRegistry containerRegistry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        string region = "was",
        string instanceType = "micro",
        global::Pulumi.Koyeb.Provider? provider = null
    )
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        _imageName =
            imageName
            ?? containerImage?.Name
            ?? throw new ArgumentException("Either imageName or containerImage must be provided.");
        _imageTag = imageTag;
        _region = region;
        _instanceType = instanceType;
        _provider = provider;
    }

    public string Name { get; }
    public ComputeServiceType ServiceType => ComputeServiceType.KoyebService;
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets.Select(x => x.secret);
    public IContainerRegistry ContainerRegistry { get; }
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
    public Output<string> ServiceUrl => _app?.Domains.Apply(GetFirstDomainUrl) ?? Output.Create("");
    public Output<string> Arn => _service?.Id ?? Output.Create("");
    public CloudProvider Provider => CloudProvider.Koyeb;
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
    public IList<PortForwardingConfiguration> PortForwardings { get; set; } =
    [new PortForwardingConfiguration { ContainerPort = 8080, Protocol = "http" }];
    public string? MemoryLimit { get; set; }
    public string? CpuLimit { get; set; }
    public KoyebScaleToZeroOptions? ScaleToZeroOptions { get; set; }

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
        _app = new global::Pulumi.Koyeb.App(
            $"{Name}-app",
            new global::Pulumi.Koyeb.AppArgs { Name = Name },
            options
        );

        _service = new global::Pulumi.Koyeb.Service(
            $"{Name}-service",
            new global::Pulumi.Koyeb.ServiceArgs
            {
                AppName = _app.Name,
                Definition = BuildDefinition(),
            },
            new CustomResourceOptions { Provider = _provider, DependsOn = [_app] }
        );

        _scaleToZeroSleepCommand = BuildScaleToZeroSleepCommand();
    }

    public IEnumerable<Resource> GetDependencies()
    {
        if (_service is null)
            return [];

        return _scaleToZeroSleepCommand is null ? [_service] : [_service, _scaleToZeroSleepCommand];
    }

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

    private global::Pulumi.Koyeb.Inputs.ServiceDefinitionArgs BuildDefinition()
    {
        var port = PortForwardings.FirstOrDefault().ContainerPort;
        if (port == 0)
            port = 8080;

        var definition = new global::Pulumi.Koyeb.Inputs.ServiceDefinitionArgs
        {
            Name = Name,
            Docker = new global::Pulumi.Koyeb.Inputs.ServiceDefinitionDockerArgs
            {
                Image = BuildImageUri(),
            },
            Type = "WEB",
            InstanceTypes =
            [
                new global::Pulumi.Koyeb.Inputs.ServiceDefinitionInstanceTypeArgs
                {
                    Type = _instanceType,
                },
            ],
            Ports =
            [
                new global::Pulumi.Koyeb.Inputs.ServiceDefinitionPortArgs
                {
                    Port = port,
                    Protocol = "http",
                },
            ],
            Regions = [_region],
            Routes =
            [
                new global::Pulumi.Koyeb.Inputs.ServiceDefinitionRouteArgs
                {
                    Path = "/",
                    Port = port,
                },
            ],
            Scalings =
            [
                new global::Pulumi.Koyeb.Inputs.ServiceDefinitionScalingArgs
                {
                    Min = MinCount,
                    Max = MaxCount,
                },
            ],
        };

        foreach (var variable in _environmentVariables)
            definition.Envs.Add(
                new global::Pulumi.Koyeb.Inputs.ServiceDefinitionEnvArgs
                {
                    Key = variable.Key,
                    Value = variable.Value,
                }
            );

        foreach (var variable in _environmentVariablesOutput)
            definition.Envs.Add(
                new global::Pulumi.Koyeb.Inputs.ServiceDefinitionEnvArgs
                {
                    Key = variable.Key,
                    Value = variable.Value,
                }
            );

        foreach (var (secret, envVarName) in _requiredSecrets)
            definition.Envs.Add(
                new global::Pulumi.Koyeb.Inputs.ServiceDefinitionEnvArgs
                {
                    Key = envVarName ?? ToEnvironmentName(secret.Name),
                    Secret = secret.Name,
                }
            );

        return definition;
    }

    private Command? BuildScaleToZeroSleepCommand()
    {
        if (
            ScaleToZeroOptions is null
            || !ScaleToZeroOptions.HasConfiguredSleepDelays
            || _app is null
            || _service is null
        )
            return null;

        var arguments = new List<string>();
        if (ScaleToZeroOptions.LightSleepDelay.HasValue)
            arguments.Add(
                $"--light-sleep-delay {FormatKoyebDuration(ScaleToZeroOptions.LightSleepDelay.Value)}"
            );

        if (ScaleToZeroOptions.DeepSleepDelay.HasValue)
            arguments.Add(
                $"--deep-sleep-delay {FormatKoyebDuration(ScaleToZeroOptions.DeepSleepDelay.Value)}"
            );

        var appName = _app.Name.Apply(ShellQuote);
        var serviceName = ShellQuote(Name);
        var updateCommand = appName.Apply(app =>
            $"koyeb services update {app}/{serviceName} --min-scale {MinCount} --max-scale {MaxCount} {string.Join(' ', arguments)} --wait"
        );

        return new Command(
            $"{Name}-scale-to-zero-sleep",
            new CommandArgs
            {
                Create = updateCommand,
                Update = updateCommand,
                Triggers = new InputList<object>
                {
                    (Input<object>)FormatKoyebDuration(ScaleToZeroOptions.LightSleepDelay),
                    (Input<object>)FormatKoyebDuration(ScaleToZeroOptions.DeepSleepDelay),
                    (Input<object>)MinCount.ToString(),
                    (Input<object>)MaxCount.ToString(),
                },
            },
            new CustomResourceOptions { DependsOn = [_service] }
        );
    }

    private Output<string> BuildImageUri() =>
        ContainerRegistry.RegistryUrlOutput.Apply(registryUrl =>
            string.IsNullOrWhiteSpace(registryUrl)
                ? $"{_imageName}:{_imageTag}"
                : $"{registryUrl.TrimEnd('/')}/{_imageName}:{_imageTag}"
        );

    private static string GetFirstDomainUrl(
        ImmutableArray<global::Pulumi.Koyeb.Outputs.AppDomain> domains
    )
    {
        var domain = domains.FirstOrDefault();
        return string.IsNullOrWhiteSpace(domain?.Name) ? string.Empty : $"https://{domain.Name}";
    }

    private static string FormatKoyebDuration(TimeSpan? duration) =>
        duration.HasValue ? FormatKoyebDuration(duration.Value) : string.Empty;

    private static string FormatKoyebDuration(TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
            return "0";

        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Koyeb sleep delays cannot be negative."
            );

        if (duration.TotalSeconds % 3600 == 0)
            return $"{(int)duration.TotalHours}h";

        if (duration.TotalSeconds % 60 == 0)
            return $"{(int)duration.TotalMinutes}m";

        return $"{(int)duration.TotalSeconds}s";
    }

    private static string ShellQuote(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string ToEnvironmentName(string name) =>
        name.Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
}
