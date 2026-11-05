using System.Collections.Immutable;
using Pulumi;
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
    private KoyebServiceSleep? _scaleToZeroSleep;

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

        _scaleToZeroSleep = BuildScaleToZeroSleep();
    }

    public IEnumerable<Resource> GetDependencies()
    {
        if (_service is null)
            return [];

        return _scaleToZeroSleep is null ? [_service] : [_service, _scaleToZeroSleep];
    }

    public IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names) =>
        names.Select(name => new DnsRecord(
            DnsRecordType.CNAME,
            name,
            ServiceUrl.Apply(ExtractHost),
            $"{name}-cname"
        ));

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
            SecretMappings = _requiredSecrets
                .GroupBy(x => x.secret.Name)
                .ToDictionary(group => group.Key, group => group.Last().envVarName),
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

    private KoyebServiceSleep? BuildScaleToZeroSleep()
    {
        if (
            ScaleToZeroOptions is null
            || !ScaleToZeroOptions.HasConfiguredSleepDelays
            || _service is null
        )
            return null;

        return new KoyebServiceSleep(
            $"{Name}-scale-to-zero-sleep",
            new KoyebServiceSleepArgs
            {
                ServiceId = _service.Id,
                LightSleepDelaySeconds = DelaySeconds(ScaleToZeroOptions.LightSleepDelay),
                DeepSleepDelaySeconds = DelaySeconds(ScaleToZeroOptions.DeepSleepDelay),
                // A redeploy by the bridged provider sends a definition without the delays.
                Triggers = [_service.Version],
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

    private static int? DelaySeconds(TimeSpan? delay)
    {
        if (delay is null)
            return null;
        if (delay < TimeSpan.Zero || delay.Value.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new ArgumentOutOfRangeException(
                nameof(delay),
                "Koyeb sleep delays must be a non-negative whole number of seconds."
            );
        return (int)delay.Value.TotalSeconds;
    }

    private static string ExtractHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var toParse = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"https://{url}";
        return Uri.TryCreate(toParse, UriKind.Absolute, out var uri) ? uri.Host : url;
    }

    private static string ToEnvironmentName(string name) =>
        name.Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
}
