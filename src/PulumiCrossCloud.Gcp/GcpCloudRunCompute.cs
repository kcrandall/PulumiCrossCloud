using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using Pulumi;
using Pulumi.Gcp.ArtifactRegistry;
using Pulumi.Gcp.CloudRun;
using Pulumi.Gcp.CloudRunV2;
using Pulumi.Gcp.CloudRunV2.Inputs;
using Pulumi.Gcp.Projects;
using Pulumi.Gcp.PubSub;
using Pulumi.Gcp.SecretManager;
using Pulumi.Gcp.ServiceAccount;
using PulumiCrossCloud.Core;
using Service = Pulumi.Gcp.CloudRunV2.Service;

namespace PulumiCrossCloud.Gcp;

public class GcpCloudRunCompute : ICompute
{
    public string Name { get; }
    public ComputeServiceType ServiceType { get; }
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets.Select(x => x.secret);
    public Output<string> Arn => _service.Name;
    public CloudProvider Provider => CloudProvider.GCP;
    private static readonly ConcurrentDictionary<
        string,
        Pulumi.Gcp.Projects.Service
    > _cloudRunApiEnabled = new();
    private Pulumi.Gcp.CloudRunV2.Service _service = null!; // Will be initialized in Deploy method
    private readonly List<(ISecret secret, string? envVarName)> _requiredSecrets = [];
    private readonly Dictionary<string, string> _environmentVariables = new();
    private readonly Dictionary<string, Output<string>> _environmentVariablesOutput = new();
    private readonly List<IPolicyStatement> _customPolicyStatements = new();

    private Pulumi.Gcp.ServiceAccount.Account _serviceAccountResource;
    private RepositoryIamMember? _registryIamMember;
    private readonly List<SecretIamMember> _secretIamMembers = new();
    private Pulumi.Gcp.CloudRunV2.ServiceIamMember? _publicAccessIamMember;
    private readonly List<string> _allowedServiceAccounts = [];
    private readonly List<Func<ICompute, Task>> _onHealthyActions = [];
    private readonly List<Func<ICompute, Task>> _onReadyActions = [];

    // Domain mapping and certificate resources
    private readonly ISet<string> _customDomains = new HashSet<string>();
    private readonly List<DomainMapping> _domainMappings = [];

    // Add public property to access the service URL
    public Output<string> ServiceUrl { get; private set; } = Output.Create(string.Empty);

    /// <summary>
    /// Gets the service account email for this Cloud Run service.
    /// </summary>
    public Output<string> ServiceAccountEmail =>
        _serviceAccountResource?.Email ?? Output.Create(string.Empty);

    /// <summary>
    /// Gets the custom domains that were registered for this service.
    /// </summary>
    public IEnumerable<string> CustomDomains => _customDomains;

    /// <summary>
    /// Gets the domain mapping resources created for custom domains.
    /// </summary>
    public IEnumerable<DomainMapping> DomainMappings => _domainMappings.AsReadOnly();

    /// <summary>
    /// Gets the public access IAM member resource that allows unauthenticated access to the service.
    /// This can be used as a dependency to ensure the service is publicly accessible before other operations.
    /// </summary>
    public Pulumi.Gcp.CloudRunV2.ServiceIamMember? PublicAccessIamMember => _publicAccessIamMember;

    /// <summary>
    /// Ingress setting for Cloud Run ("all", "internal", "internal-and-cloud-load-balancing")
    /// </summary>
    public string IngressSetting { get; set; } = "INGRESS_TRAFFIC_ALL";

    /// <summary>
    /// The maximum number of concurrent requests allowed per container instance. Default is 80. If null, uses GCP default.
    /// </summary>
    public int? ContainerConcurrency { get; set; } = null;

    public int MinCount { get; set; } = 0;
    public int MaxCount { get; set; } = 1;

    public string? MemoryLimit { get; set; }
    public string? CpuLimit { get; set; }

    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;

    public IList<PortForwardingConfiguration> PortForwardings { get; set; } =
        new List<PortForwardingConfiguration>
        {
            new PortForwardingConfiguration { ContainerPort = 8080 },
        };

    private readonly string _imageName = string.Empty; // Will be set in constructor
    private readonly string _imageTag;
    private readonly IContainerImage? _containerImage;
    private readonly Pulumi.Gcp.Provider? _provider;

    public GcpCloudRunCompute(
        string name,
        IContainerRegistry containerRegistry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        string? serviceAccountEmail = null,
        Pulumi.Gcp.Provider? provider = null
    )
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.CloudRun;
        _imageTag = imageTag;
        _containerImage = containerImage;
        _provider = provider;

        // Set _imageName based on parameters
        if (imageName is not null)
            _imageName = imageName;
        else if (containerImage is not null)
            _imageName = containerImage.Name;
        else
            throw new ArgumentException(
                "Either imageName or containerImage must be provided for GCP Cloud Run compute resource."
            );

        var projectId = GcpProjectConfig.ProjectId;
        // Ensure required APIs are enabled for this project
        if (!_cloudRunApiEnabled.ContainsKey(projectId))
        {
            var enableRun = GcpProjectServices.EnsureEnabled("run.googleapis.com");
            _cloudRunApiEnabled[projectId] = enableRun;
        }
        // Cloud Run provider sometimes queries Compute regions; ensure Compute API is enabled
        _ = GcpProjectServices.EnsureEnabled("compute.googleapis.com");
        // Create or use provided service account
        if (string.IsNullOrEmpty(serviceAccountEmail))
        {
            var iamApi = GcpProjectServices.EnsureEnabled("iam.googleapis.com");
            _serviceAccountResource = new Pulumi.Gcp.ServiceAccount.Account(
                $"cloudrun-sa-{name}",
                new Pulumi.Gcp.ServiceAccount.AccountArgs
                {
                    AccountId = $"cr-sa-{name}",
                    Project = projectId,
                },
                new CustomResourceOptions { DependsOn = new[] { iamApi }, Provider = provider }
            );
        }
        else
            _serviceAccountResource = Pulumi.Gcp.ServiceAccount.Account.Get(
                $"cloudrun-sa-imported-{name}",
                serviceAccountEmail,
                null,
                new CustomResourceOptions { Provider = provider }
            );
    }

    public void AddRequiredSecret(ISecret secret, string? environmentVariableName = null)
    {
        _requiredSecrets.Add((secret, environmentVariableName));
    }

    public void AddPolicyStatement(IPolicyStatement statement)
    {
        _customPolicyStatements.Add(statement);
    }

    public void Build()
    {
        var projectId = GcpProjectConfig.ProjectId;
        var region = GcpProjectConfig.Region;

        // Simplify image URI construction to avoid complex Output.Apply chains
        var imageUri = ContainerRegistry is GcpContainerRegistry gcpRegistry
            ? gcpRegistry.RegistryUrlOutput.Apply(url => $"{url}/{_imageName}:{_imageTag}")
            : Output.Create($"{region}-docker.pkg.dev/{projectId}/{Name}/{_imageName}:{_imageTag}");

        var portConfig = PortForwardings.FirstOrDefault();

        // Warn if more than one port forwarding is configured (Cloud Run only supports one port)
        if (PortForwardings.Count > 1)
            Log.Warn(
                $"Warning: Cloud Run only supports one port. Found {PortForwardings.Count} port forwardings. Only the first port ({PortForwardings[0].ContainerPort}) will be used."
            );

        // Build environment variables list
        var envs = new List<ServiceTemplateContainerEnvArgs>();

        // Add regular environment variables
        foreach (var kvp in EnvironmentVariables)
            envs.Add(
                new Pulumi.Gcp.CloudRunV2.Inputs.ServiceTemplateContainerEnvArgs
                {
                    Name = kvp.Key,
                    Value = kvp.Value,
                }
            );

        // Add output environment variables
        foreach (var kvp in _environmentVariablesOutput)
            envs.Add(
                new Pulumi.Gcp.CloudRunV2.Inputs.ServiceTemplateContainerEnvArgs
                {
                    Name = kvp.Key,
                    Value = kvp.Value,
                }
            );

        // Add secret environment variables
        foreach (
            var (secret, envVarName) in _requiredSecrets.Where(s =>
                s.envVarName is not null && s.secret is GcpSecret
            )
        )
        {
            var gcpSecret = (GcpSecret)secret;
            // In v2, use the *secret ID* (your GcpSecret.Name) and a concrete version (e.g., "1")
            envs.Add(
                new ServiceTemplateContainerEnvArgs
                {
                    Name = envVarName!,
                    ValueSource = new ServiceTemplateContainerEnvValueSourceArgs
                    {
                        SecretKeyRef = new ServiceTemplateContainerEnvValueSourceSecretKeyRefArgs
                        {
                            Secret = gcpSecret.Name,
                            Version = "latest",
                        },
                    },
                }
            );
        }

        // Pick a port (Cloud Run defaults to 8080)
        var hcPort =
            (PortForwardings != null && PortForwardings.Count > 0)
                ? PortForwardings[0].ContainerPort
                : 8080;

        ServiceTemplateContainerStartupProbeArgs? startup = null;
        ServiceTemplateContainerLivenessProbeArgs? live = null;

        if (HealthCheckConfiguration is not null)
        {
            startup = new ServiceTemplateContainerStartupProbeArgs
            {
                // Using HTTP GET — you can switch to TcpSocket if you prefer
                HttpGet = new ServiceTemplateContainerStartupProbeHttpGetArgs
                {
                    Path = HealthCheckConfiguration.Path ?? "/",
                    Port = hcPort,
                },
                TimeoutSeconds =
                    HealthCheckConfiguration.StartupTimeoutSeconds
                    ?? HealthCheckConfiguration.TimeoutSeconds,
                PeriodSeconds =
                    HealthCheckConfiguration.StartupIntervalSeconds
                    ?? HealthCheckConfiguration.IntervalSeconds,
                FailureThreshold =
                    HealthCheckConfiguration.StartupUnhealthyThreshold
                    ?? HealthCheckConfiguration.UnhealthyThreshold,
                InitialDelaySeconds = 0,
            };

            live = new ServiceTemplateContainerLivenessProbeArgs
            {
                HttpGet = new ServiceTemplateContainerLivenessProbeHttpGetArgs
                {
                    Path = HealthCheckConfiguration.Path ?? "/",
                    Port = hcPort,
                },
                TimeoutSeconds = HealthCheckConfiguration.TimeoutSeconds,
                PeriodSeconds = HealthCheckConfiguration.IntervalSeconds,
                FailureThreshold = HealthCheckConfiguration.UnhealthyThreshold,
            };
        }

        var container = new ServiceTemplateContainerArgs
        {
            Image = imageUri,
            Envs = envs.ToArray(),
            Ports = new ServiceTemplateContainerPortsArgs { ContainerPort = hcPort },
            StartupProbe = startup!,
            LivenessProbe = live!,
            Resources = new ServiceTemplateContainerResourcesArgs
            {
                Limits = new InputMap<string>
                {
                    { "memory", MemoryLimit ?? "1Gi" },
                    { "cpu", CpuLimit ?? "1" },
                },
            },
        };
        var serviceAccountEmail = _serviceAccountResource?.Email;

        var template = new ServiceTemplateArgs
        {
            Containers = new[] { container },
            ServiceAccount = serviceAccountEmail!,
            Scaling = new ServiceTemplateScalingArgs
            {
                MinInstanceCount = MinCount,
                MaxInstanceCount = MaxCount,
            },
            MaxInstanceRequestConcurrency = ContainerConcurrency,
        };

        var serviceArgs = new Pulumi.Gcp.CloudRunV2.ServiceArgs
        {
            Name = Name,
            Project = projectId,
            Location = region,
            Ingress = IngressSetting, // v2 top-level field
            Template = template,
            // Service-level scaling (separate from template scaling) - set explicitly to prevent drift
            Scaling = new ServiceScalingArgs { MinInstanceCount = MinCount },
            Traffics =
            {
                new ServiceTrafficArgs
                {
                    Type = "TRAFFIC_TARGET_ALLOCATION_TYPE_LATEST",
                    Percent = 100,
                },
            },
            DeletionProtection = false,
        };

        // Build dependsOn list
        var dependsOn = new List<Resource> { _cloudRunApiEnabled[projectId] };

        if (
            ContainerRegistry is GcpContainerRegistry readyRegistry
            && readyRegistry.Resource is not null
        )
            dependsOn.Add(readyRegistry.Resource);

        if (_containerImage?.Resource is not null)
            dependsOn.Add(_containerImage.Resource);

        // Prefer depending on SecretVersion if you can expose it from GcpSecret;
        // if not, at least depend on the Secret resource.
        foreach (var s in _requiredSecrets)
            if (s.secret is GcpSecret gs)
                dependsOn.Add(gs.Resource);

        if (
            ContainerRegistry is GcpContainerRegistry gcpContainerRegistry
            && serviceAccountEmail is not null
        )
        {
            _registryIamMember = new RepositoryIamMember(
                $"cloudrun-registry-pull-{Name}",
                new RepositoryIamMemberArgs
                {
                    Project = projectId,
                    Location = region,
                    Repository = gcpContainerRegistry.RepositoryId,
                    Role = "roles/artifactregistry.reader",
                    Member = Output.Format($"serviceAccount:{serviceAccountEmail}"),
                },
                new CustomResourceOptions { Provider = _provider }
            );
            dependsOn.Add(_registryIamMember);
        }

        // // Add secret resources to dependsOn so they are created before Cloud Run
        foreach (var secret in _requiredSecrets)
            if (secret.secret is GcpSecret gcpSecret)
                dependsOn.Add(gcpSecret.Resource);

        // // IAM bindings for secrets
        foreach (var secret in _requiredSecrets)
            if (secret.secret is GcpSecret gcpSecret && serviceAccountEmail is not null)
            {
                var secretIam = new SecretIamMember(
                    $"cloudrun-secret-read-{Name}-{gcpSecret.Name}",
                    new SecretIamMemberArgs
                    {
                        Project = projectId,
                        SecretId = gcpSecret.Name,
                        Role = "roles/secretmanager.secretAccessor",
                        Member = Output.Format($"serviceAccount:{serviceAccountEmail}"),
                    },
                    new CustomResourceOptions
                    {
                        DependsOn = new[] { gcpSecret.Resource },
                        Provider = _provider,
                    }
                );
                _secretIamMembers.Add(secretIam);
                dependsOn.Add(secretIam);
            }

        // Process custom policy statements using the GcpPolicyManager
        foreach (var statement in _customPolicyStatements.OfType<GcpPolicyStatement>())
            GcpPolicyManager.ApplyPolicy(
                $"iam-{Name}",
                statement,
                serviceAccountEmail!,
                projectId,
                _provider,
                dependsOn
            );

        _service = new Pulumi.Gcp.CloudRunV2.Service(
            Name,
            serviceArgs,
            new CustomResourceOptions { DependsOn = dependsOn, Provider = _provider }
        );
        ServiceUrl = _service.Uri; // <-- v2: direct Uri output

        // Create public access IAM member AFTER the service
        _publicAccessIamMember = new Pulumi.Gcp.CloudRunV2.ServiceIamMember(
            $"cloudrun-public-access-{Name}",
            new Pulumi.Gcp.CloudRunV2.ServiceIamMemberArgs
            {
                Project = projectId,
                Location = region,
                Name = Name, // Use local variable for name
                Role = "roles/run.invoker",
                Member = "allUsers",
            },
            new CustomResourceOptions { DependsOn = new[] { _service }, Provider = _provider }
        );

        foreach (var allowed in _allowedServiceAccounts)
            _ = new Pulumi.Gcp.CloudRunV2.ServiceIamMember(
                $"cloudrun-allow-invoke-{Name}-{allowed.Replace(':', '-').Replace('@', '-')}",
                new Pulumi.Gcp.CloudRunV2.ServiceIamMemberArgs
                {
                    Project = projectId,
                    Location = region,
                    Name = Name,
                    Role = "roles/run.invoker",
                    Member = $"serviceAccount:{allowed}",
                },
                new CustomResourceOptions { DependsOn = new[] { _service }, Provider = _provider }
            );

        // Create domain mappings for custom domains if specified
        if (_customDomains.Any())
            foreach (var domain in _customDomains)
            {
                var domainMapping = new DomainMapping(
                    $"domain-mapping-{Name}-{domain.Replace('.', '-')}",
                    new DomainMappingArgs
                    {
                        Project = projectId,
                        Location = region,
                        Name = domain,
                        Metadata = new Pulumi.Gcp.CloudRun.Inputs.DomainMappingMetadataArgs
                        {
                            Namespace = projectId,
                        },
                        Spec = new Pulumi.Gcp.CloudRun.Inputs.DomainMappingSpecArgs
                        {
                            RouteName = _service.Name,
                        },
                    },
                    new CustomResourceOptions
                    {
                        DependsOn = new List<Resource> { _service, _publicAccessIamMember! },
                        DeleteBeforeReplace = true,
                        Provider = _provider,
                    }
                );

                _domainMappings.Add(domainMapping);

                Log.Info($"Created domain mapping for {domain} -> Cloud Run service {Name}");
            }
    }

    public void AddEnvironmentVariable(string name, string value)
    {
        _environmentVariables[name] = value;
    }

    public void AddEnvironmentVariable(string name, Output<string> value)
    {
        _environmentVariablesOutput[name] = value;
    }

    public void AddEnvironmentVariables(IDictionary<string, string> variables)
    {
        foreach (var kvp in variables)
            _environmentVariables[kvp.Key] = kvp.Value;
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
            switch (permission)
            {
                case ComputePermissions.GetStatus:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/run.viewer" },
                            Resources = new[] { Arn },
                        }
                    );
                    break;
                case ComputePermissions.Scale:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/run.developer" },
                            Resources = new[] { Arn },
                        }
                    );
                    break;
                case ComputePermissions.GetLogs:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/logging.viewer" },
                            Resources = new[] { Output.Create("*") },
                        }
                    );
                    break;
            }

        return statements;
    }

    public void AllowCloudRunServiceAccess(string serviceAccountEmail)
    {
        if (!_allowedServiceAccounts.Contains(serviceAccountEmail))
            _allowedServiceAccounts.Add(serviceAccountEmail);
    }

    /// <summary>
    /// Gets dependencies including the service itself and public access IAM member.
    /// This ensures that external operations wait for the service to be deployed and publicly accessible.
    /// </summary>
    public IEnumerable<Resource> GetDependencies()
    {
        var dependencies = new List<Resource>();

        // Add the service itself as a dependency (if it exists)
        if (_service is not null)
            dependencies.Add(_service);

        // Add public access IAM member if it exists
        if (_publicAccessIamMember is not null)
            dependencies.Add(_publicAccessIamMember);

        return dependencies;
    }

    public IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names)
    {
        // For Cloud Run custom domains, Google requires DNS records:
        // - Subdomains: CNAME to ghs.googlehosted.com
        // - Apex domains: A records to Google front-end anycast IPs
        // Here we assume subdomains (typical for app/api), and emit CNAME -> ghs.googlehosted.com
        var target = Output.Create("ghs.googlehosted.com");
        return names
            .Select(name => new DnsRecord(DnsRecordType.CNAME, name, target, $"{name}-cname"))
            .ToList();
    }

    /// <summary>
    /// Registers custom domains and returns the DNS records needed for domain mapping.
    /// This method internally calls GetRequiredDnsRecords and stores the custom domains
    /// for later creation. When Build() is called, actual GCP Cloud Run domain mappings
    /// will be created with automatic SSL certificate provisioning.
    /// </summary>
    /// <param name="customDomains">List of custom domain names (e.g., app.example.com) to map to this Cloud Run service.</param>
    /// <returns>The DNS records that need to be configured in your DNS provider.</returns>
    public IEnumerable<DnsRecord> RegisterDnsMappingAndCertificates(
        IEnumerable<string> customDomains
    )
    {
        foreach (var domain in customDomains)
            _customDomains.Add(domain);

        // Emit required DNS for Cloud Run domain mapping: CNAME -> ghs.googlehosted.com
        return GetRequiredDnsRecords(customDomains);
    }

    /// <summary>
    /// Gets all environment variables and secrets configured for this compute resource.
    /// Returns a response object containing dictionaries of environment variables and secret mappings.
    /// </summary>
    /// <returns>ComputeConfigurationResponse containing all configuration details.</returns>
    public ComputeConfigurationResponse GetConfiguration()
    {
        var response = new ComputeConfigurationResponse
        {
            EnvironmentVariables = new Dictionary<string, string>(_environmentVariables),
            EnvironmentVariablesOutput = new Dictionary<string, Output<string>>(
                _environmentVariablesOutput
            ),
            RequiredSecrets = _requiredSecrets.Select(x => x.secret).ToList(),
            SecretMappings = _requiredSecrets.ToDictionary(x => x.secret.Name, x => x.envVarName),
        };

        return response;
    }

    public void OnHealthy(params Func<ICompute, Task>[] actions)
    {
        _onHealthyActions.AddRange(actions);
        ServiceUrl.Apply(async url =>
        {
            var healthPath = HealthCheckConfiguration?.Path ?? string.Empty;
            var healthUrl = $"{url}{(healthPath.StartsWith("/") ? healthPath : "/" + healthPath)}";
            using var http = new HttpClient();
            var healthy = false;
            for (int i = 0; i < 60; i++)
            {
                try
                {
                    var response = await http.GetAsync(healthUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        healthy = true;
                        foreach (var action in _onHealthyActions)
                            await action(this);

                        break;
                    }
                }
                catch
                {
                    // ignore connection failures, retry
                }
                await Task.Delay(5000);
            }
            if (!healthy)
                Log.Error(
                    $"Cloud Run health check failed for {Name} at {healthUrl} after 5 minutes."
                );
        });
    }

    public void OnReady(params Func<ICompute, Task>[] actions)
    {
        _onReadyActions.AddRange(actions);
        // Register ready logic to run after deployment
        ServiceUrl.Apply(async url =>
        {
            // Fire all registered actions after service is deployed and URL is available
            foreach (var action in _onReadyActions)
                await action(this);
        });
    }
}
