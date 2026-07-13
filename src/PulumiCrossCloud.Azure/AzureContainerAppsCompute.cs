// using Pulumi;
// using PulumiCrossCloud.Core;
//
// namespace PulumiCrossCloud.Azure;
//
// public class AzureContainerAppsCompute : ICompute
// {
//     private readonly List<ISecret> _requiredSecrets;
//     private readonly Dictionary<string, string> _environmentVariables = new();
//     private readonly Dictionary<string, Output<string>> _environmentVariablesOutput = new();
//     private readonly List<IPolicyStatement> _customPolicyStatements = new();
//
//     public AzureContainerAppsCompute(string name, IContainerRegistry containerRegistry, string imageTag = "latest")
//     {
//         Name = name;
//         ContainerRegistry = containerRegistry;
//         ServiceType = ComputeServiceType.ContainerApps;
//         _requiredSecrets = new List<ISecret>();
//
//         // Azure Container Apps implementation would go here
//         Arn = Output.Create($"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.App/containerApps/{name}");
//
//         // Handle the registry URL as an Output
//         var imageUri = containerRegistry is AzureContainerRegistry azureRegistry
//             ? azureRegistry.RegistryUrlOutput.Apply(url => $"{url}/{name}:{imageTag}")
//             : Output.Create($"PLACEHOLDER_REGISTRY_URL/{name}:{imageTag}");
//     }
//
//     public string Name { get; }
//     public ComputeServiceType ServiceType { get; }
//     public IContainerRegistry ContainerRegistry { get; }
//     public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets;
//     public Output<string> Arn { get; private set; }  // Changed to Output<string>
//     public CloudProvider Provider => CloudProvider.Azure;
//     public IDictionary<string, string> EnvironmentVariables => _environmentVariables;
//
//     public int MinCount { get; set; } = 1;
//     public int MaxCount { get; set; } = 1;
//     public Output<string> ServiceUrl { get; private set; } = Output.Create("");
//     public IHealthCheckConfiguration? HealthCheckConfiguration { get; set; }
//
//     public void AttachRole(IRole role)
//     {
//         if (role is not AzureManagedIdentity managedIdentity)
//         {
//             throw new ArgumentException("Azure compute resources require AzureManagedIdentity, not generic IRole");
//         }
//
//         var policies = new List<IPolicyStatement>();
//
//         foreach (var secret in RequiredSecrets)
//         {
//             policies.AddRange(secret.GetPolicy(SecretPermissions.Read));
//         }
//
//         policies.AddRange(ContainerRegistry.GetPolicy(ContainerRegistryPermissions.Pull));
//
//         // Create role assignments for this managed identity
//         managedIdentity.AttachInlinePolicy($"{Name}-permissions", policies);
//
//         // TODO: Configure the Container App to use this managed identity
//         // This would involve setting the identity property on the Container App
//     }
//
//     public void AddRequiredSecret(ISecret secret, string? environmentVariableName = null)
//     {
//         _requiredSecrets.Add(secret);
//     }
//
//     public void AddPolicyStatement(IPolicyStatement statement)
//     {
//         _customPolicyStatements.Add(statement);
//     }
//
//     public void Build()
//     {
//         // Aggregate all policy statements for managed identity
//         // (if using managed identity, would attach here)
//         // Prepare secrets as environment variables
//         var envVars = new Dictionary<string, string>(_environmentVariables);
//         foreach (var secret in RequiredSecrets)
//         {
//             var secretKey = $"SECRET_{secret.Name.ToUpper().Replace("-", "_")}";
//             // Reference the secret by name for Azure Container Apps
//             envVars[secretKey] = $"{{{secret.Name}}}";
//         }
//         // Health check configuration for Azure Container Apps
//         var probes = HealthCheckConfiguration != null
//             ? new []
//             {
//                 new Pulumi.AzureNative.App.Inputs.ContainerAppProbeArgs
//                 {
//                     Type = "Liveness",
//                     HttpGet = new Pulumi.AzureNative.App.Inputs.ContainerAppProbeHttpGetArgs
//                     {
//                         Path = HealthCheckConfiguration.Path,
//                         Port = 8080
//                     },
//                     InitialDelaySeconds = HealthCheckConfiguration.HealthyThreshold,
//                     PeriodSeconds = HealthCheckConfiguration.IntervalSeconds,
//                     TimeoutSeconds = HealthCheckConfiguration.TimeoutSeconds,
//                     FailureThreshold = HealthCheckConfiguration.UnhealthyThreshold
//                 }
//             }
//             : null;
//         var probeList = new Pulumi.InputList<Pulumi.AzureNative.App.Inputs.ContainerAppProbeArgs>();
//         if (probes != null)
//         {
//             foreach (var probe in probes)
//             {
//                 probeList.Add(probe);
//             }
//         }
//         var container = new Pulumi.AzureNative.App.Inputs.ContainerArgs
//         {
//             Name = Name,
//             Image = ContainerRegistry is AzureContainerRegistry acr ? acr.RegistryUrlOutput.Apply(url => $"{url}/{Name}:latest") : Output.Create($"PLACEHOLDER_REGISTRY_URL/{Name}:latest"),
//             Env = envVars.Select(kvp => new Pulumi.AzureNative.App.Inputs.EnvironmentVarArgs
//             {
//                 Name = kvp.Key,
//                 Value = kvp.Value
//             }).ToArray(),
//             Probes = probeList,
//         };
//         var containerApp = new Pulumi.AzureNative.App.ContainerApp(Name, new Pulumi.AzureNative.App.ContainerAppArgs
//         {
//             ResourceGroupName = "rg", // Replace with actual resource group
//             ContainerAppName = Name,
//             Configuration = new Pulumi.AzureNative.App.Inputs.ConfigurationArgs
//             {
//                 Secrets = RequiredSecrets.Select(s => new Pulumi.AzureNative.App.Inputs.SecretArgs
//                 {
//                     Name = s.Name,
//                     Value = s is AzureSecret azs ? azs.Value : ""
//                 }).ToArray(),
//                 ActiveRevisionsMode = "Multiple"
//             },
//             Template = new Pulumi.AzureNative.App.Inputs.TemplateArgs
//             {
//                 Containers = new[] { container },
//                 Scale = new Pulumi.AzureNative.App.Inputs.ScaleArgs
//                 {
//                     MinReplicas = MinCount,
//                     MaxReplicas = MaxCount
//                 }
//             }
//         });
//         Arn = containerApp.Id;
//         ServiceUrl = containerApp.LatestRevisionFqdn;
//     }
//
//     public void AddEnvironmentVariable(string name, string value)
//     {
//         _environmentVariables[name] = value;
//     }
//
//     public void AddEnvironmentVariable(string name, Output<string> value)
//     {
//         _environmentVariablesOutput[name] = value;
//     }
//
//     public void AddEnvironmentVariables(IDictionary<string, string> variables)
//     {
//         foreach (var kvp in variables)
//         {
//             _environmentVariables[kvp.Key] = kvp.Value;
//         }
//     }
//
//     public void Start() => throw new NotImplementedException("Azure Container Apps implementation needed");
//     public void Stop() => throw new NotImplementedException();
//     public void Scale(int replicas)
//     {
//         // Update desired count, but respect MinCount/MaxCount
//         var count = Math.Max(MinCount, Math.Min(MaxCount, replicas));
//         // Azure Container Apps scaling logic would go here
//     }
//
//     public void ConfigurePermissions()
//     {
//         // Implement Azure role assignments for required secrets, registry, and custom policy statements
//         // This is a placeholder for actual logic
//     }
//
//     public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
//     {
//         var statements = new List<IPolicyStatement>();
//         foreach (var permission in permissions)
//         {
//             switch (permission)
//             {
//                 case ComputePermissions.GetStatus:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.App/containerApps/read" },
//                         Resources = new[] { Arn }
//                     });
//                     break;
//                 case ComputePermissions.Scale:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.App/containerApps/write" },
//                         Resources = new[] { Arn }
//                     });
//                     break;
//                 case ComputePermissions.GetLogs:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.Insights/logs/read" },
//                         Resources = new[] { Output.Create("*") }
//                     });
//                     break;
//             }
//         }
//         return statements;
//     }
//     public void OnHealthy(params Func<ICompute, Task>[] actions) {}
//     public void OnReady(params Func<ICompute, Task>[] actions) {}
//     public IEnumerable<DnsRecord> GetRequiredDnsRecords(IEnumerable<string> names) => [];
//     public IList<PortForwardingConfiguration> PortForwardings { get; set; } = [];
//     public string? MemoryLimit { get; set; }
//     public string? CpuLimit { get; set; }
// }
