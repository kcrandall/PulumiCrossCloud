using Pulumi;
using PulumiCrossCloud.Core;
using DO = global::Pulumi.DigitalOcean;

namespace PulumiCrossCloud.DigitalOcean;

public sealed class DigitalOceanAppPlatformCompute : ICompute
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
    private readonly DO.Provider? _provider;
    private readonly IContainerImage? _containerImage;
    private readonly List<string> _customDomains = [];
    private DO.App? _app;

    public DigitalOceanAppPlatformCompute(
        string name,
        IContainerRegistry containerRegistry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        string region = "nyc",
        string instanceSizeSlug = "apps-s-1vcpu-1gb",
        DO.Provider? provider = null
    )
    {
        if (containerRegistry.Provider != CloudProvider.DigitalOcean)
        {
            throw new ArgumentException("Registry must be a DigitalOcean registry.", nameof(containerRegistry));
        }

        Name = name;
        ContainerRegistry = containerRegistry;
        _imageName = imageName ?? containerImage?.Name ?? throw new ArgumentException("Either imageName or containerImage must be provided.");
        _imageTag = imageTag;
        _region = region;
        InstanceSizeSlug = instanceSizeSlug;
        _provider = provider;
        _containerImage = containerImage;
    }

    public string Name { get; }
    public ComputeServiceType ServiceType => ComputeServiceType.DigitalOceanAppPlatform;
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets.Select(x => x.secret);
    public IContainerRegistry ContainerRegistry { get; }
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
    public Output<string> ServiceUrl => _app?.LiveUrl ?? Output.Create("");
    public Output<string> Arn => _app?.Id ?? Output.Create("");
    public CloudProvider Provider => CloudProvider.DigitalOcean;
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
    public IList<PortForwardingConfiguration> PortForwardings { get; set; } =
    [
        new PortForwardingConfiguration { ContainerPort = 8080, Protocol = "http" }
    ];
    public string? MemoryLimit { get; set; }
    public string? CpuLimit { get; set; }
    public string InstanceSizeSlug { get; set; }

    public void AddRequiredSecret(ISecret secret, string? environmentVariableName = null) =>
        _requiredSecrets.Add((secret, environmentVariableName));

    public void AddEnvironmentVariable(string name, string value) => _environmentVariables[name] = value;

    public void AddEnvironmentVariable(string name, Output<string> value) =>
        _environmentVariablesOutput[name] = value;

    public void AddEnvironmentVariables(IDictionary<string, string> variables)
    {
        foreach (var variable in variables)
        {
            _environmentVariables[variable.Key] = variable.Value;
        }
    }

    public void AddPolicyStatement(IPolicyStatement statement) => _customPolicyStatements.Add(statement);

    public void Build()
    {
        var dependsOn = new List<Resource> { ContainerRegistry.Resource };
        if (_containerImage?.Resource is not null)
        {
            dependsOn.Add(_containerImage.Resource);
        }

        dependsOn.AddRange(_requiredSecrets.Select(secret => secret.secret.Resource));

        _app = new DO.App(
            $"{Name}-app",
            new DO.AppArgs
            {
                Spec = new DO.Inputs.AppSpecArgs
                {
                    Name = Name,
                    Region = _region,
                    Services = [BuildServiceArgs()],
                    Ingress = BuildIngressArgs(),
                    DomainNames = BuildDomainNameArgs()
                }
            },
            new CustomResourceOptions { Provider = _provider, DependsOn = dependsOn }
        );
    }

    public IEnumerable<Resource> GetDependencies() => _app is null ? [] : [_app];

    public IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names) =>
        names.Select(name => new DnsRecord(DnsRecordType.CNAME, name, ServiceUrl.Apply(ExtractHost), $"{name}-cname"));

    public IEnumerable<DnsRecord> RegisterDnsMappingAndCertificates(IEnumerable<string> customDomains)
    {
        foreach (var domain in customDomains)
        {
            if (!_customDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                _customDomains.Add(domain);
            }
        }

        return GetRequiredDnsRecords(customDomains);
    }

    public ComputeConfigurationResponse GetConfiguration() =>
        new()
        {
            EnvironmentVariables = new Dictionary<string, string>(_environmentVariables),
            EnvironmentVariablesOutput = new Dictionary<string, Output<string>>(_environmentVariablesOutput),
            RequiredSecrets = _requiredSecrets.Select(x => x.secret).ToList(),
            SecretMappings = _requiredSecrets.ToDictionary(x => x.secret.Name, x => x.envVarName)
        };

    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions) => [];

    public void OnHealthy(params Func<ICompute, Task>[] actions) => _onHealthyActions.AddRange(actions);

    public void OnReady(params Func<ICompute, Task>[] actions) => _onReadyActions.AddRange(actions);

    private DO.Inputs.AppSpecServiceArgs BuildServiceArgs()
    {
        var port = PortForwardings.FirstOrDefault().ContainerPort;
        if (port == 0)
        {
            port = 8080;
        }

        var service = new DO.Inputs.AppSpecServiceArgs
        {
            Name = Name,
            HttpPort = port,
            Image = new DO.Inputs.AppSpecServiceImageArgs
            {
                RegistryType = "DOCR",
                Repository = _imageName,
                Tag = _imageTag
            },
            InstanceSizeSlug = InstanceSizeSlug
        };

        if (MaxCount > MinCount)
        {
            service.Autoscaling = new DO.Inputs.AppSpecServiceAutoscalingArgs
            {
                MinInstanceCount = MinCount,
                MaxInstanceCount = MaxCount
            };
        }
        else
        {
            service.InstanceCount = Math.Max(1, MinCount);
        }

        if (HealthCheckConfiguration is not null)
        {
            service.HealthCheck = new DO.Inputs.AppSpecServiceHealthCheckArgs
            {
                HttpPath = HealthCheckConfiguration.Path,
                Port = port,
                InitialDelaySeconds = HealthCheckConfiguration.StartupTimeoutSeconds,
                PeriodSeconds = HealthCheckConfiguration.IntervalSeconds,
                TimeoutSeconds = HealthCheckConfiguration.TimeoutSeconds,
                FailureThreshold = HealthCheckConfiguration.UnhealthyThreshold,
                SuccessThreshold = HealthCheckConfiguration.HealthyThreshold
            };
        }

        foreach (var variable in _environmentVariables)
        {
            service.Envs.Add(new DO.Inputs.AppSpecServiceEnvArgs
            {
                Key = variable.Key,
                Value = variable.Value,
                Type = "GENERAL",
                Scope = "RUN_TIME"
            });
        }

        foreach (var variable in _environmentVariablesOutput)
        {
            service.Envs.Add(new DO.Inputs.AppSpecServiceEnvArgs
            {
                Key = variable.Key,
                Value = variable.Value,
                Type = "GENERAL",
                Scope = "RUN_TIME"
            });
        }

        foreach (var (secret, envVarName) in _requiredSecrets)
        {
            service.Envs.Add(new DO.Inputs.AppSpecServiceEnvArgs
            {
                Key = envVarName ?? ToEnvironmentName(secret.Name),
                Value = secret.Value.Apply(value => value ?? string.Empty),
                Type = "SECRET",
                Scope = "RUN_TIME"
            });
        }

        return service;
    }

    private DO.Inputs.AppSpecIngressArgs BuildIngressArgs() =>
        new()
        {
            Rules =
            [
                new DO.Inputs.AppSpecIngressRuleArgs
                {
                    Match = new DO.Inputs.AppSpecIngressRuleMatchArgs
                    {
                        Path = new DO.Inputs.AppSpecIngressRuleMatchPathArgs { Prefix = "/" }
                    },
                    Component = new DO.Inputs.AppSpecIngressRuleComponentArgs { Name = Name }
                }
            ]
        };

    private InputList<DO.Inputs.AppSpecDomainNameArgs> BuildDomainNameArgs()
    {
        var domains = new InputList<DO.Inputs.AppSpecDomainNameArgs>();
        foreach (var domain in _customDomains)
        {
            domains.Add(new DO.Inputs.AppSpecDomainNameArgs
            {
                Name = domain,
                Type = "PRIMARY"
            });
        }

        return domains;
    }

    private static string ExtractHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var toParse = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"https://{url}";
        return Uri.TryCreate(toParse, UriKind.Absolute, out var uri) ? uri.Host : url;
    }

    private static string ToEnvironmentName(string name) =>
        name.Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();
}
