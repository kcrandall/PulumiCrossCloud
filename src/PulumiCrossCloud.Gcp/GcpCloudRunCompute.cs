using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpCloudRunCompute : ICompute
{
    private Pulumi.Gcp.CloudRun.Service _service;
    private readonly List<ISecret> _requiredSecrets;
    private readonly Dictionary<string, string> _environmentVariables = new();
    private readonly List<IPolicyStatement> _customPolicyStatements = new();
    
    public GcpCloudRunCompute(string name, IContainerRegistry containerRegistry, string imageTag = "latest")
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.CloudRun;
        _requiredSecrets = new List<ISecret>();
        // Service will be created after Build (not here)
    }
    
    public string Name { get; }
    public ComputeServiceType ServiceType { get; }
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets;
    public Output<string> Arn => _service.Name;
    public CloudProvider Provider => CloudProvider.GCP;
    
    // Add public property to access the service URL
    public Output<string> ServiceUrl => _service.Statuses.Apply(statuses => 
        statuses.FirstOrDefault()?.Url ?? "");
    
    public void AttachRole(IRole role)
    {
        if (role is not GcpServiceAccount serviceAccount)
        {
            throw new ArgumentException("GCP compute resources require GcpServiceAccount, not generic IRole");
        }
    
        // In GCP, you'd typically:
        // 1. Set the service account on the Cloud Run service
        // 2. Create IAM bindings to give the service account permissions
    
        var policies = new List<IPolicyStatement>();
    
        foreach (var secret in RequiredSecrets)
        {
            policies.AddRange(secret.GetPolicy(SecretPermissions.Read));
        }
    
        policies.AddRange(ContainerRegistry.GetPolicy(ContainerRegistryPermissions.Pull));
    
        // For now, just log what would be created
        Console.WriteLine($"Would create IAM bindings for service account {serviceAccount.ServiceAccountEmail}");
        Console.WriteLine($"Policies needed: {policies.Count()}");
    
        // TODO: Actually create IAM bindings in GCP
    }
    
    public void AddRequiredSecret(ISecret secret)
    {
        _requiredSecrets.Add(secret);
    }

    public void AddPolicyStatement(IPolicyStatement statement)
    {
        _customPolicyStatements.Add(statement);
    }

    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;

    public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }

    public void Build()
    {
        var projectId = GcpProjectConfig.ProjectId;
        var region = GcpProjectConfig.Region;
        var imageUri = ContainerRegistry is GcpContainerRegistry gcpRegistry
            ? gcpRegistry.RegistryUrlOutput.Apply(url => $"{url}/{Name}:latest")
            : Output.Create($"{region}-docker.pkg.dev/{projectId}/{Name}:latest");
        var containerArgs = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerArgs
        {
            Image = imageUri,
            Ports = new[]
            {
                new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerPortArgs
                {
                    ContainerPort = 8080
                }
            },
            Envs = EnvironmentVariables.Select(kvp => new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerEnvArgs
            {
                Name = kvp.Key,
                Value = kvp.Value
            }).ToArray()
        };
        if (HealthCheckConfiguration != null)
        {
            containerArgs.StartupProbe = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerStartupProbeArgs
            {
                InitialDelaySeconds = 0,
                TimeoutSeconds = HealthCheckConfiguration.TimeoutSeconds,
                PeriodSeconds = HealthCheckConfiguration.IntervalSeconds,
                FailureThreshold = HealthCheckConfiguration.UnhealthyThreshold,
                HttpGet = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerStartupProbeHttpGetArgs
                {
                    Path = HealthCheckConfiguration.Path,
                    Port = 8080
                }
            };
            containerArgs.LivenessProbe = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerLivenessProbeArgs
            {
                FailureThreshold = HealthCheckConfiguration.UnhealthyThreshold,
                HttpGet = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecContainerLivenessProbeHttpGetArgs
                {
                    Path = HealthCheckConfiguration.Path,
                    Port = 8080
                }
            };
        }
        var templateArgs = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateArgs
        {
            Spec = new Pulumi.Gcp.CloudRun.Inputs.ServiceTemplateSpecArgs
            {
                Containers = new[] { containerArgs },
            }
        };
        var serviceArgs = new Pulumi.Gcp.CloudRun.ServiceArgs
        {
            Location = region,
            Project = projectId,
            Template = templateArgs,
        };
        _service = new Pulumi.Gcp.CloudRun.Service(Name, serviceArgs);
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

    public void Start() { /* Start logic */ }
    public void Stop() { /* Stop logic */ }
    public void Scale(int replicas)
    {
        // Update desired count, but respect MinCount/MaxCount
        var count = Math.Max(MinCount, Math.Min(MaxCount, replicas));
        // GCP Cloud Run scaling logic would go here
    }
    
    public IDictionary<string, string> EnvironmentVariables => _environmentVariables;

    public void ConfigurePermissions()
    {
        // Implement GCP IAM bindings for required secrets, registry, and custom policy statements
        // This is a placeholder for actual logic
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case ComputePermissions.GetStatus:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/run.viewer" },
                        Resources = new[] { Arn }
                    });
                    break;
                case ComputePermissions.Scale:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/run.developer" },
                        Resources = new[] { Arn }
                    });
                    break;
                case ComputePermissions.GetLogs:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/logging.viewer" },
                        Resources = new[] { Output.Create("*") }
                    });
                    break;
            }
        }
        return statements;
    }
}