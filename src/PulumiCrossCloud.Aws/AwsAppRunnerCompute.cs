using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsAppRunnerCompute : ICompute
{
    private readonly Pulumi.Aws.AppRunner.Service _service;
    private readonly List<ISecret> _requiredSecrets;
    
    public AwsAppRunnerCompute(string name, IContainerRegistry containerRegistry, string imageTag = "latest")
    {
        Name = name;
        ContainerRegistry = containerRegistry;
        ServiceType = ComputeServiceType.AppRunner;
        _requiredSecrets = new List<ISecret>();
        
        // We need to handle the registry URL as an Output
        var imageIdentifier = containerRegistry is AwsContainerRegistry awsRegistry 
            ? awsRegistry.RegistryUrlOutput.Apply(url => $"{url}:{imageTag}")
            : Output.Create($"PLACEHOLDER_REGISTRY_URL:{imageTag}");
        
        _service = new Pulumi.Aws.AppRunner.Service(name, new Pulumi.Aws.AppRunner.ServiceArgs
        {
            ServiceName = name,
            SourceConfiguration = new Pulumi.Aws.AppRunner.Inputs.ServiceSourceConfigurationArgs
            {
                ImageRepository = new Pulumi.Aws.AppRunner.Inputs.ServiceSourceConfigurationImageRepositoryArgs
                {
                    ImageConfiguration = new Pulumi.Aws.AppRunner.Inputs.ServiceSourceConfigurationImageRepositoryImageConfigurationArgs
                    {
                        Port = "8080"
                    },
                    ImageIdentifier = imageIdentifier,
                    ImageRepositoryType = "ECR"
                },
                AutoDeploymentsEnabled = true
            }
        });
    }
    
    public string Name { get; }
    public ComputeServiceType ServiceType { get; }
    public IContainerRegistry ContainerRegistry { get; }
    public IEnumerable<ISecret> RequiredSecrets => _requiredSecrets;
    public Output<string> Arn => _service.Arn;
    public CloudProvider Provider => CloudProvider.AWS;
    
    public Output<string> ServiceUrl => _service.ServiceUrl;
    
    public void AttachRole(IRole role)
    {
        var policies = new List<IPolicyStatement>();
        
        foreach (var secret in RequiredSecrets)
        {
            policies.AddRange(secret.GetPolicy(SecretPermissions.Read));
        }
        
        policies.AddRange(ContainerRegistry.GetPolicy(ContainerRegistryPermissions.Pull, ContainerRegistryPermissions.GetAuthToken));
        
        role.AttachInlinePolicy($"{Name}-policy", policies);
    }
    
    public void AddRequiredSecret(ISecret secret)
    {
        _requiredSecrets.Add(secret);
    }
    
    public void Start() { /* App Runner auto-starts */ }
    public void Stop() { /* Implementation for pausing service */ }
    public void Scale(int replicas) { /* App Runner auto-scales, but can set min/max */ }
    public void UpdateConfiguration(IDictionary<string, object> config) { /* Update service configuration */ }
    
    public IEnumerable<IPolicyStatement> GetPolicy(params ComputePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case ComputePermissions.GetStatus:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "apprunner:DescribeService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.Start:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "apprunner:ResumeService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.Stop:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "apprunner:PauseService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.UpdateConfiguration:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "apprunner:UpdateService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case ComputePermissions.GetLogs:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "logs:GetLogEvents", "logs:DescribeLogStreams" },
                        Resources = new Output<string>[] { Output.Create($"arn:aws:logs:*:*:log-group:/aws/apprunner/{Name}/*") }
                    });
                    break;
                case ComputePermissions.Scale:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "apprunner:UpdateService" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
            }
        }
        
        return statements;
    }
}