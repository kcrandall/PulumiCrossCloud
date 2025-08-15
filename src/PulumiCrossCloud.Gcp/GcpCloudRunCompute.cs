using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using Pulumi;
using Pulumi.Gcp.ArtifactRegistry;
using Pulumi.Gcp.CloudRun.Inputs;
using Pulumi.Gcp.Projects;
using Pulumi.Gcp.SecretManager;
using Pulumi.Gcp.ServiceAccount;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpCloudRunCompute : ICompute
{
    public string Name { get; }
    public ComputeServiceType ServiceType { get; }
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets.Select(x => x.secret);
    public Output<string> Arn => _service.Name;
    public CloudProvider Provider => CloudProvider.GCP;
    private static readonly ConcurrentDictionary<string, Service> _cloudRunApiEnabled = new();
    private Pulumi.Gcp.CloudRun.Service _service;
    private readonly List<(ISecret secret, string? envVarName)> _requiredSecrets = [];
    private readonly Dictionary<string, string> _environmentVariables = new();
    private readonly List<IPolicyStatement> _customPolicyStatements = new();
    private GcpServiceAccount _serviceAccount;
    private Pulumi.Gcp.ServiceAccount.Account _serviceAccountResource;
    private RepositoryIamMember? _registryIamMember;
    private readonly List<SecretIamMember> _secretIamMembers = new();
    private readonly List<string> _allowedServiceAccounts = [];
    private readonly List<Func<ICompute, Task>> _onHealthyActions = [];
    private readonly List<Func<ICompute, Task>> _onReadyActions = [];

    // Add public property to access the service URL
    public Output<string> ServiceUrl =>
        _service.Statuses.Apply(statuses => statuses.FirstOrDefault()?.Url ?? "");

    /// <summary>
    /// Ingress setting for Cloud Run ("all", "internal", "internal-and-cloud-load-balancing")
    /// </summary>
    public string IngressSetting { get; set; } = "all";

    /// <summary>
    /// The maximum number of concurrent requests allowed per container instance. Default is 80. If null, uses GCP default.
    /// </summary>
    public int? ContainerConcurrency { get; set; } = null;

    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;

    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;

    public IList<PortForwardingConfiguration> PortForwardings { get; set; } =
        new List<PortForwardingConfiguration>
        {
            new PortForwardingConfiguration { ContainerPort = 8080 }
        };

    private readonly string _imageName;
    private readonly string _imageTag;
    private readonly IContainerImage? _containerImage;

    public GcpCloudRunCompute(
        string name,
        IContainerRegistry containerRegistry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        string? serviceAccountEmail = null
    )
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.CloudRun;
        _imageName = imageName;
        _imageTag = imageTag;
        _containerImage = containerImage;
        if (imageName is null && containerImage is not null)
            _imageName = containerImage.Name;
        else if (imageName is null && containerImage is null)
            throw new ArgumentException(
                "Either imageName or containerImage must be provided for GCP Cloud Run compute resource."
            );

        var projectId = GcpProjectConfig.ProjectId;
        // Ensure Cloud Run API is enabled for this project (via central manager)
        if (!_cloudRunApiEnabled.ContainsKey(projectId))
        {
            var enableApi = GcpProjectServices.EnsureEnabled("run.googleapis.com");
            _cloudRunApiEnabled[projectId] = enableApi;
        }
        // Create or use provided service account
        if (string.IsNullOrEmpty(serviceAccountEmail))
        {
            var iamApi = GcpProjectServices.EnsureEnabled("iam.googleapis.com");
            _serviceAccountResource = new Pulumi.Gcp.ServiceAccount.Account(
                $"cloudrun-sa-{name}",
                new Pulumi.Gcp.ServiceAccount.AccountArgs
                {
                    AccountId = $"cloudrn-sa-{name}",
                    Project = projectId
                },
                new CustomResourceOptions { DependsOn = new[] { iamApi } }
            );
        }
        else
        {
            _serviceAccountResource = Pulumi.Gcp.ServiceAccount.Account.Get(
                $"cloudrun-sa-imported-{name}",
                serviceAccountEmail
            );
        }
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
        var imageUri = ContainerRegistry is GcpContainerRegistry gcpRegistry
            ? gcpRegistry.RegistryUrlOutput.Apply(url => $"{url}/{_imageName}:{_imageTag}")
            : Output.Create($"{region}-docker.pkg.dev/{projectId}/{Name}/{_imageName}:{_imageTag}");
        var portConfig = PortForwardings.FirstOrDefault();
        List<Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerEnvArgs> envs =
        [
            .. EnvironmentVariables.Select(
                kvp => new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerEnvArgs
                {
                    Name = kvp.Key,
                    Value = kvp.Value
                }
            ),
            .. _requiredSecrets
                .Where(x => x.envVarName is not null && x.secret is GcpSecret)
                .Select(x => new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerEnvArgs
                {
                    Name = x.envVarName!,
                    ValueFrom = new ServiceTemplateSpecContainerEnvValueFromArgs
                    {
                        SecretKeyRef = new ServiceTemplateSpecContainerEnvValueFromSecretKeyRefArgs
                        {
                            Name = ((GcpSecret)x.secret).Name,
                            Key = "latest" // Use the latest version of the secret
                        }
                    }
                })
        ];
        var containerArgs = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerArgs
        {
            Image = imageUri,
            Ports =
            [
                new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerPortArgs
                {
                    ContainerPort = portConfig.ContainerPort,
                }
            ],
            Envs = envs.ToArray()
        };
        if (HealthCheckConfiguration is not null)
        {
            // Use startup probe only if supported and values provided, fallback to liveness values if null
            containerArgs.StartupProbe =
                new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerStartupProbeArgs
                {
                    InitialDelaySeconds = 0,
                    TimeoutSeconds =
                        HealthCheckConfiguration.StartupTimeoutSeconds
                        ?? HealthCheckConfiguration.TimeoutSeconds,
                    PeriodSeconds =
                        HealthCheckConfiguration.StartupIntervalSeconds
                        ?? HealthCheckConfiguration.IntervalSeconds,
                    FailureThreshold =
                        HealthCheckConfiguration.StartupUnhealthyThreshold
                        ?? HealthCheckConfiguration.UnhealthyThreshold,
                    HttpGet =
                        new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerStartupProbeHttpGetArgs
                        {
                            Path = HealthCheckConfiguration.Path,
                            Port = 8080
                        }
                };
            containerArgs.LivenessProbe =
                new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerLivenessProbeArgs
                {
                    FailureThreshold = HealthCheckConfiguration.UnhealthyThreshold,
                    HttpGet =
                        new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerLivenessProbeHttpGetArgs
                        {
                            Path = HealthCheckConfiguration.Path,
                            Port = 8080
                        },
                    TimeoutSeconds = HealthCheckConfiguration.TimeoutSeconds,
                    PeriodSeconds = HealthCheckConfiguration.IntervalSeconds,
                };
        }
        var serviceAccountEmail = _serviceAccountResource is not null
            ? _serviceAccountResource.Email
            : null;
        var trafficArgs = new Pulumi.Gcp.CloudRun.Inputs.ServiceTrafficArgs
        {
            LatestRevision = true,
            Percent = 100
        };
        var templateArgs = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateArgs
        {
            Spec = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecArgs
            {
                Containers = [containerArgs],
                ServiceAccountName = serviceAccountEmail,
                ContainerConcurrency = ContainerConcurrency is not null
                    ? ContainerConcurrency
                    : null
            }
        };
        var serviceArgs = new Pulumi.Gcp.CloudRun.ServiceArgs
        {
            Location = region,
            Project = projectId,
            Metadata = new Pulumi.Gcp.CloudRun.Inputs.ServiceMetadataArgs
            {
                Annotations = new InputMap<string>
                {
                    { "run.googleapis.com/ingress", IngressSetting }
                }
            },
            Template = templateArgs,
            Traffics = [trafficArgs],
        };
        var dependsOn = new List<Resource> { _cloudRunApiEnabled[projectId] };
        // Ensure the container registry (including any default image import) is ready before creating the service
        if (ContainerRegistry is GcpContainerRegistry readyRegistry)
        {
            if (readyRegistry.ReadyResource is not null)
                dependsOn.Add(readyRegistry.ReadyResource);
        }
        // Add container image resource to dependsOn if provided
        if (_containerImage?.Resource is not null && _containerImage is GcpContainerImage gcpImage)
        {
            dependsOn.Add(gcpImage.Resource);
        }
        // IAM binding for registry
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
                    Member = Output.Format($"serviceAccount:{serviceAccountEmail}")
                }
            );
            dependsOn.Add(_registryIamMember);
        }
        // Add secret resources to dependsOn so they are created before Cloud Run
        foreach (var secret in _requiredSecrets)
        {
            if (secret.secret is GcpSecret gcpSecret)
            {
                dependsOn.Add(gcpSecret.Resource);
            }
        }
        // IAM bindings for secrets
        foreach (var secret in _requiredSecrets)
        {
            if (secret.secret is GcpSecret gcpSecret && serviceAccountEmail is not null)
            {
                var secretIam = new SecretIamMember(
                    $"cloudrun-secret-read-{Name}-{gcpSecret.Name}",
                    new SecretIamMemberArgs
                    {
                        Project = projectId,
                        SecretId = gcpSecret.Name,
                        Role = "roles/secretmanager.secretAccessor",
                        Member = Output.Format($"serviceAccount:{serviceAccountEmail}")
                    }
                );
                _secretIamMembers.Add(secretIam);
                dependsOn.Add(secretIam);
            }
        }
        // IAM bindings for allowed Cloud Run service accounts
        foreach (var allowed in _allowedServiceAccounts)
        {
            var invokerBinding = new Pulumi.Gcp.CloudRun.IamMember(
                $"cloudrun-allow-invoke-{Name}-{allowed}",
                new Pulumi.Gcp.CloudRun.IamMemberArgs
                {
                    Service = Name,
                    Location = region,
                    Role = "roles/run.invoker",
                    Member = Output.Format($"serviceAccount:{allowed}")
                }
            );
            dependsOn.Add(invokerBinding);
        }
        _service = new Pulumi.Gcp.CloudRun.Service(
            Name,
            serviceArgs,
            new CustomResourceOptions { DependsOn = dependsOn }
        );
    }

    public void AddEnvironmentVariable(string name, string value)
    {
        _environmentVariables[name] = value;
    }

    public void AddEnvironmentVariables(IDictionary<string, string> variables)
    {
        foreach (var kvp in variables)
        {
            _environmentVariables[kvp.Key] = kvp.Value;
        }
    }

    public void Start()
    {
        // Use Google.Apis.CloudRun.v1 to set minScale annotation for start
        var projectId = GcpProjectConfig.ProjectId;
        var region = GcpProjectConfig.Region;
        var serviceName = $"projects/{projectId}/locations/{region}/services/{Name}";
        var cloudRunService = new Google.Apis.CloudRun.v1.CloudRunService(
            new Google.Apis.Services.BaseClientService.Initializer()
        );
        var getRequest = cloudRunService.Namespaces.Services.Get(serviceName);
        var service = getRequest.Execute();
        service.Metadata.Annotations ??= new Dictionary<string, string>();
        service.Metadata.Annotations["autoscaling.knative.dev/minScale"] =
            MinCount > 0 ? MinCount.ToString() : "1";
        var updateRequest = cloudRunService.Namespaces.Services.ReplaceService(
            service,
            serviceName
        );
        updateRequest.Execute();
    }

    public void Stop()
    {
        // Use Google.Apis.CloudRun.v1 to set minScale annotation for stop
        var projectId = GcpProjectConfig.ProjectId;
        var region = GcpProjectConfig.Region;
        var serviceName = $"projects/{projectId}/locations/{region}/services/{Name}";
        var cloudRunService = new Google.Apis.CloudRun.v1.CloudRunService(
            new Google.Apis.Services.BaseClientService.Initializer()
        );
        var getRequest = cloudRunService.Namespaces.Services.Get(serviceName);
        var service = getRequest.Execute();
        service.Metadata.Annotations ??= new Dictionary<string, string>();
        service.Metadata.Annotations["autoscaling.knative.dev/minScale"] = "0";
        var updateRequest = cloudRunService.Namespaces.Services.ReplaceService(
            service,
            serviceName
        );
        updateRequest.Execute();
    }

    public void Scale(int replicas)
    {
        // Update desired count, but respect MinCount/MaxCount
        var count = Math.Max(MinCount, Math.Min(MaxCount, replicas));
        // GCP Cloud Run scaling logic would go here
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case ComputePermissions.GetStatus:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/run.viewer" },
                            Resources = new[] { Arn }
                        }
                    );
                    break;
                case ComputePermissions.Scale:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/run.developer" },
                            Resources = new[] { Arn }
                        }
                    );
                    break;
                case ComputePermissions.GetLogs:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/logging.viewer" },
                            Resources = new[] { Output.Create("*") }
                        }
                    );
                    break;
            }
        }
        return statements;
    }

    public void Deploy()
    {
        var projectId = GcpProjectConfig.ProjectId;
        var region = GcpProjectConfig.Region;
        var serviceName = $"projects/{projectId}/locations/{region}/services/{Name}";
        var cloudRunService = new Google.Apis.CloudRun.v1.CloudRunService(
            new Google.Apis.Services.BaseClientService.Initializer()
        );
        var getRequest = cloudRunService.Namespaces.Services.Get(serviceName);
        var service = getRequest.Execute();
        if (
            service.Spec is not null
            && service.Spec.Template is not null
            && service.Spec.Template.Spec is not null
            && service.Spec.Template.Spec.Containers is not null
            && service.Spec.Template.Spec.Containers.Count > 0
        )
        {
            var container = service.Spec.Template.Spec.Containers[0];
            if (ContainerRegistry is GcpContainerRegistry gcpRegistry)
            {
                gcpRegistry.RegistryUrlOutput.Apply(url =>
                {
                    container.Image = $"{url}:latest";
                    var updateRequest = cloudRunService.Namespaces.Services.ReplaceService(
                        service,
                        serviceName
                    );
                    updateRequest.Execute();
                    return 0; // dummy return for Apply
                });
            }
            else
            {
                container.Image = $"{region}-docker.pkg.dev/{projectId}/{Name}:latest";
                var updateRequest = cloudRunService.Namespaces.Services.ReplaceService(
                    service,
                    serviceName
                );
                updateRequest.Execute();
            }
        }
    }

    public void AllowCloudRunServiceAccess(string serviceAccountEmail)
    {
        if (!_allowedServiceAccounts.Contains(serviceAccountEmail))
        {
            _allowedServiceAccounts.Add(serviceAccountEmail);
        }
    }

    public IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names)
    {
        // For GCP Cloud Run, typically a CNAME record is needed to map a custom domain to the service URL.
        // Example: CNAME app.example.com -> <service>.a.run.app
        // ServiceUrl is Output<string>, so we use Output<string> in DnsRecord.

        // For each provided domain name, return a CNAME record pointing to the Cloud Run service URL
        return names.Select(name => new DnsRecord(DnsRecordType.CNAME, name, ServiceUrl)).ToList();
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
                        {
                            await action(this);
                        }
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
            {
                Log.Error(
                    $"Cloud Run health check failed for {Name} at {healthUrl} after 5 minutes."
                );
            }
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
            {
                await action(this);
            }
        });
    }
}
