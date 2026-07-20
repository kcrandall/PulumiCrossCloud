using System.Net.Http;
using System.Text.Json;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

/// <summary>
/// Runs a container on AWS Fargate using either Amazon ECS or Amazon EKS.
/// </summary>
public partial class AwsFargateCompute : ICompute
{
    private readonly List<(ISecret Secret, string? EnvironmentVariableName)> _requiredSecrets = [];
    private readonly Dictionary<string, string> _environmentVariables = [];
    private readonly Dictionary<string, Output<string>> _environmentVariablesOutput = [];
    private readonly List<IPolicyStatement> _customPolicyStatements = [];
    private readonly List<Func<ICompute, Task>> _onHealthyActions = [];
    private readonly List<Func<ICompute, Task>> _onReadyActions = [];
    private readonly List<Resource> _dependencies = [];
    private readonly string _imageTag;
    private readonly IContainerImage? _containerImage;
    private readonly AwsFargateOptions _options;
    private readonly Pulumi.Aws.Provider? _provider;

    private Output<string> _arn = Output.Create(string.Empty);
    private Output<string> _serviceUrl = Output.Create(string.Empty);
    private Output<string> _dnsTarget = Output.Create(string.Empty);
    private bool _built;

    public AwsFargateCompute(
        string name,
        IContainerRegistry containerRegistry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        AwsFargateOptions? options = null,
        Pulumi.Aws.Provider? provider = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageTag);
        ArgumentNullException.ThrowIfNull(containerRegistry);

        if (containerRegistry.Provider != CloudProvider.AWS)
            throw new ArgumentException(
                "AWS Fargate requires an AWS container registry.",
                nameof(containerRegistry)
            );
        if (
            containerImage is not null
            && !ReferenceEquals(containerImage.Registry, containerRegistry)
        )
            throw new ArgumentException(
                "containerImage must have been created from the containerRegistry passed to AWS Fargate.",
                nameof(containerImage)
            );

        if (string.IsNullOrWhiteSpace(imageName) && containerImage is null)
            throw new ArgumentException(
                "Either imageName or containerImage must be provided for AWS Fargate compute.",
                nameof(imageName)
            );
        if (
            containerImage is null
            && !string.Equals(imageName, containerRegistry.Name, StringComparison.Ordinal)
        )
            throw new ArgumentException(
                $"AWS container registry '{containerRegistry.Name}' represents one ECR repository. "
                    + "imageName must match registry.Name, or containerImage must be supplied from CreateContainerImage/PushImage.",
                nameof(imageName)
            );

        Name = name;
        ContainerRegistry = containerRegistry;
        _imageTag = imageTag;
        _containerImage = containerImage;
        _options = options ?? new AwsFargateOptions();
        _provider = provider;

        if (!Enum.IsDefined(_options.Backend))
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.Backend,
                "Unsupported AWS Fargate backend."
            );
    }

    public string Name { get; }
    public ComputeServiceType ServiceType => ComputeServiceType.Fargate;
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets.Select(item => item.Secret);
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
    public Output<string> Arn => _arn;
    public CloudProvider Provider => CloudProvider.AWS;
    public Output<string> ServiceUrl => _serviceUrl;
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    public string? MemoryLimit { get; set; }
    public string? CpuLimit { get; set; }
    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
    public IList<PortForwardingConfiguration> PortForwardings { get; set; } =
    [
        new PortForwardingConfiguration { ContainerPort = 8080 },
    ];

    public void AddRequiredSecret(ISecret secret, string? environmentVariableName = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        _requiredSecrets.Add((secret, environmentVariableName));
    }

    public void AddEnvironmentVariable(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _environmentVariables[name] = value;
    }

    public void AddEnvironmentVariable(string name, Output<string> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _environmentVariablesOutput[name] = value;
    }

    public void AddEnvironmentVariables(IDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        foreach (var (name, value) in variables)
            AddEnvironmentVariable(name, value);
    }

    public void AddPolicyStatement(IPolicyStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        _customPolicyStatements.Add(statement);
    }

    public ComputeConfigurationResponse GetConfiguration() =>
        new()
        {
            EnvironmentVariables = new Dictionary<string, string>(_environmentVariables),
            EnvironmentVariablesOutput = new Dictionary<string, Output<string>>(
                _environmentVariablesOutput
            ),
            RequiredSecrets = RequiredSecrets.ToArray(),
            SecretMappings = _requiredSecrets
                .GroupBy(item => item.Secret.Name)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().EnvironmentVariableName
                ),
        };

    public void Build()
    {
        if (_built)
            return;

        ValidateCommonConfiguration();

        switch (_options.Backend)
        {
            case AwsFargateBackend.EcsExpress:
                BuildEcsExpressService();
                break;
            case AwsFargateBackend.Ecs:
                BuildEcsService();
                break;
            case AwsFargateBackend.Eks:
                BuildEksService();
                break;
        }

        _built = true;
        foreach (var action in _onReadyActions)
            ScheduleReadyAction(action);
        foreach (var action in _onHealthyActions)
            ScheduleHealthyAction(action);
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            switch (_options.Backend)
            {
                case AwsFargateBackend.EcsExpress:
                    AddEcsExpressPolicy(statements, permission);
                    break;
                case AwsFargateBackend.Ecs:
                    AddEcsPolicy(statements, permission);
                    break;
                case AwsFargateBackend.Eks:
                    AddEksPolicy(statements, permission);
                    break;
            }
        }

        return statements;
    }

    public IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names) =>
        names.Select(name =>
            new DnsRecord(DnsRecordType.CNAME, name, _dnsTarget, $"{name}-cname")
        );

    public IEnumerable<Resource> GetDependencies() => _dependencies.Distinct();

    public void OnReady(params Func<ICompute, Task>[] actions)
    {
        _onReadyActions.AddRange(actions);
        if (_built)
            foreach (var action in actions)
                ScheduleReadyAction(action);
    }

    public void OnHealthy(params Func<ICompute, Task>[] actions)
    {
        _onHealthyActions.AddRange(actions);
        if (_built)
            foreach (var action in actions)
                ScheduleHealthyAction(action);
    }

    private void ScheduleReadyAction(Func<ICompute, Task> action) =>
        ServiceUrl.Apply(async _ => await action(this));

    private void ScheduleHealthyAction(Func<ICompute, Task> action) =>
        ServiceUrl.Apply(async url =>
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            var healthUrl = $"{url.TrimEnd('/')}{NormalizeHealthPath(HealthCheckConfiguration?.Path)}";
            using var http = new HttpClient();
            for (var attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    var response = await http.GetAsync(healthUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        await action(this);
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // The load balancer may not be reachable while the service is converging.
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            Pulumi.Log.Error(
                $"Fargate health check failed for {Name} at {healthUrl} after 5 minutes."
            );
        });

    private void ValidateCommonConfiguration()
    {
        if (MinCount < 0)
            throw new InvalidOperationException("MinCount cannot be negative.");
        if (MaxCount < 1)
            throw new InvalidOperationException("MaxCount must be at least one.");
        if (MaxCount < MinCount)
            throw new InvalidOperationException("MaxCount cannot be less than MinCount.");

        if (PortForwardings.Count == 0)
            PortForwardings.Add(new PortForwardingConfiguration { ContainerPort = 8080 });

        foreach (var forwarding in PortForwardings)
        {
            if (forwarding.ContainerPort is < 1 or > 65535)
                throw new InvalidOperationException(
                    $"Container port {forwarding.ContainerPort} is outside the valid range."
                );
        }
    }

    private Pulumi.Aws.Iam.Role CreateRole(string resourceName, string trustedService) =>
        new(
            resourceName,
            new Pulumi.Aws.Iam.RoleArgs
            {
                AssumeRolePolicy = JsonSerializer.Serialize(
                    new
                    {
                        Version = "2012-10-17",
                        Statement = new[]
                        {
                            new
                            {
                                Effect = "Allow",
                                Principal = new { Service = trustedService },
                                Action = "sts:AssumeRole",
                            },
                        },
                    }
                ),
                Tags = ToInputMap(_options.Tags),
            },
            new CustomResourceOptions { Provider = _provider }
        );

    private Output<string> CreatePolicyDocument(IEnumerable<IPolicyStatement> statements)
    {
        var statementList = statements.ToList();
        var resources = statementList.SelectMany(statement => statement.Resources).ToArray();

        return Output.All(resources).Apply(resolvedResources =>
        {
            var resourceIndex = 0;
            var nativeStatements = new List<object>();

            foreach (var statement in statementList)
            {
                var resourceCount = statement.Resources.Count();
                nativeStatements.Add(
                    new
                    {
                        Effect = statement.Effect,
                        Action = statement.Actions.ToArray(),
                        Resource = resolvedResources
                            .Skip(resourceIndex)
                            .Take(resourceCount)
                            .ToArray(),
                        Condition = statement.Conditions.Count > 0
                            ? statement.Conditions
                            : null,
                    }
                );
                resourceIndex += resourceCount;
            }

            return JsonSerializer.Serialize(
                new { Version = "2012-10-17", Statement = nativeStatements }
            );
        });
    }

    private int GetPrimaryPort()
    {
        var forwarding = PortForwardings.FirstOrDefault();
        return forwarding.ContainerPort == 0 ? 8080 : forwarding.ContainerPort;
    }

    private static string GetSecretEnvironmentVariableName(
        (ISecret Secret, string? EnvironmentVariableName) item
    ) =>
        item.EnvironmentVariableName
        ?? $"SECRET_{item.Secret.Name.ToUpperInvariant().Replace('-', '_')}";

    private Output<string> GetContainerImageUri()
    {
        if (_containerImage is null)
            return ContainerRegistry.RegistryUrlOutput.Apply(url => $"{url}:{_imageTag}");

        var deploymentResult = _containerImage.DeploymentResult;
        if (deploymentResult is null)
            throw new InvalidOperationException(
                $"Container image '{_containerImage.Name}' must be pushed before building Fargate compute '{Name}'."
            );

        return deploymentResult.Apply(result =>
        {
            if (string.IsNullOrWhiteSpace(result.ImageUrl))
                throw new InvalidOperationException(
                    $"Container image '{_containerImage.Name}' did not produce an image URL."
                );
            return result.ImageUrl;
        });
    }

    private static InputMap<string> ToInputMap(IDictionary<string, string>? values)
    {
        var result = new InputMap<string>();
        if (values is null)
            return result;
        foreach (var (key, value) in values)
            result.Add(key, value);
        return result;
    }

    private static string NormalizeHealthPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";
        return path.StartsWith('/') ? path : $"/{path}";
    }

    private static string EnsureUrlScheme(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;
        return endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? endpoint.TrimEnd('/')
            : $"https://{endpoint.TrimEnd('/')}";
    }

    private static string GetDnsTarget(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;
        var url = EnsureUrlScheme(endpoint);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : endpoint;
    }

    partial void BuildEcsExpressService();
    partial void AddEcsExpressPolicy(
        List<IPolicyStatement> statements,
        ComputePermissions permission
    );

    partial void BuildEcsService();
    partial void AddEcsPolicy(List<IPolicyStatement> statements, ComputePermissions permission);

    partial void BuildEksService();
    partial void AddEksPolicy(List<IPolicyStatement> statements, ComputePermissions permission);
}
