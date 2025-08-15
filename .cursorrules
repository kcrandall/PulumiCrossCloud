# GitHub Copilot Instructions for PulumiCrossCloud
- Use file-scoped namespaces.
- Prefer using `is null` and `is not null` instead of `== null` and `!= null` in if statements and other places where possible.
- Prefer initializing arrays/lists with 
- Always use collection expressions with square brackets [] instead of new() when initializing arrays, lists, and collections in C#:
    - Use: `List<string> list = ["item1", "item2"];`
    - Instead of: `var list = new List<string> {"item1", "item2"};`
    - Use: `string[] array = ["a", "b", "c"];`
    - Instead of: `string[] array = new string[] {"a", "b", "c"};`
    - Use: `var emptyList = new List<string>();` for empty collections when type inference is needed


## Project Overview
PulumiCrossCloud is a .NET 9.0 library that provides cloud-agnostic infrastructure abstractions for Pulumi with automatic IAM policy generation. It allows developers to write infrastructure code once and deploy to AWS, Azure, or GCP.

## Architecture Patterns

### Core Principles
- **Cloud Agnostic**: Same interface works across AWS, Azure, and GCP
- **Type Safe**: Resource-specific permission enums prevent errors
- **Auto Policies**: Automatically generates least-privilege IAM policies
- **Pulumi Native**: Built on top of Pulumi's Output<T> system

### Project Structure
```
PulumiCrossCloud/
├── src/
│   ├── PulumiCrossCloud.Core/          # Interfaces & abstractions
│   ├── PulumiCrossCloud.Aws/           # AWS implementations
│   ├── PulumiCrossCloud.Azure/         # Azure implementations  
│   ├── PulumiCrossCloud.Gcp/           # GCP implementations
│   └── PulumiCrossCloud.Extensions/    # Helper extensions
├── samples/
│   ├── PulumiCrossCloud.Samples.Aws/       # AWS example projects
│   ├── PulumiCrossCloud.Samples.Azure/     # Azure example projects
│   └── PulumiCrossCloud.Samples.Gcp/       # GCP example projects
├── tests/
│   ├── PulumiCrossCloud.Core.Tests/        # Core library tests
│   ├── PulumiCrossCloud.Aws.Tests/         # AWS implementation tests
│   └── PulumiCrossCloud.Integration.Tests/ # Cross-cloud integration tests
├── docs/                                   # Documentation files
├── .github/
│   ├── workflows/                           # CI/CD workflows
│   └── copilot-instructions.md              # GitHub Copilot guidelines
├── Directory.Build.props                    # Shared MSBuild properties
├── .gitignore
├── .copilotignore
├── LICENSE
├── README.md
└── PulumiCrossCloud.sln                    # Solution file
```

### Key Interfaces
- `ICloudResource` - Base for all cloud resources
- `ISecret` - Secret management abstraction
- `IContainerRegistry` - Container registry abstraction
- `ICompute` - Compute service abstraction (App Runner, Fargate, Cloud Run)
- `IRole` - IAM role abstraction
- `IPolicyStatement` - Cloud-agnostic policy representation

## Code Generation Guidelines

### 1. Output<T> Handling
Always use `Output<string>` for resource identifiers and URLs:
```csharp
// ✅ Correct
public Output<string> Arn => _resource.Arn;
public Output<string> RegistryUrlOutput => _registry.RepositoryUrl;

// ❌ Avoid
public string Arn => _resource.Arn.Apply(x => x).Result; // Blocks async
```

### 2. Policy Statement Creation
Use cloud-specific policy statements with Output<string> resources:
```csharp
// ✅ Correct pattern
statements.Add(new AwsPolicyStatement
{
    Effect = "Allow",
    Actions = new[] { "secretsmanager:GetSecretValue" },
    Resources = new Output<string>[] { Arn }
});
```

### 3. Permission Enums
Each resource type has specific permission enums:
- `SecretPermissions` - Read, Write, Delete, List, GetMetadata
- `ContainerRegistryPermissions` - Pull, Push, Delete, List, GetMetadata, ManageRepository, GetAuthToken
- `ComputePermissions` - Start, Stop, Restart, Terminate, GetStatus, GetLogs, Connect, UpdateConfiguration, Scale

### 4. Factory Pattern
Use factory pattern for cloud-specific resource creation:
```csharp
public class AwsCloudResourceFactory : CloudResourceFactory
{
    public override ISecret CreateSecret(string name, string value)
    {
        return new AwsSecret(name, value);
    }
}
```

### 5. Cross-Cloud Compatibility
Ensure implementations work consistently across clouds:
```csharp
// AWS
var awsCompute = awsFactory.CreateCompute("app", ComputeServiceType.AppRunner, registry);

// GCP  
var gcpCompute = gcpFactory.CreateCompute("app", ComputeServiceType.CloudRun, registry);

// Both use the same ICompute interface
```

## Implementation Patterns

### Resource Implementation Template
```csharp
public class CloudProviderResourceType : IResourceInterface
{
    private readonly ActualPulumiResource _resource;
    
    public CloudProviderResourceType(string name, /* other params */)
    {
        Name = name;
        _resource = new ActualPulumiResource(name, new ResourceArgs
        {
            // Configuration
        });
    }
    
    public string Name { get; }
    public Output<string> Arn => _resource.Arn; // or .Id or .Name
    public CloudProvider Provider => CloudProvider.ProviderName;
    
    public IEnumerable<IPolicyStatement> GetPolicy(params ResourcePermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case ResourcePermissions.Action:
                    statements.Add(new CloudProviderPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "service:action" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
            }
        }
        
        return statements;
    }
}
```

### Common Cloud Actions

#### AWS IAM Actions
- Secrets: `secretsmanager:GetSecretValue`, `secretsmanager:PutSecretValue`
- ECR: `ecr:GetDownloadUrlForLayer`, `ecr:BatchGetImage`, `ecr:GetAuthorizationToken`
- ECS: `ecs:DescribeServices`, `ecs:UpdateService`
- App Runner: `apprunner:DescribeService`, `apprunner:UpdateService`

#### Azure RBAC Actions
- Key Vault: `Microsoft.KeyVault/vaults/secrets/getSecret/action`
- ACR: `Microsoft.ContainerRegistry/registries/pull/read`
- Container Apps: `Microsoft.App/containerApps/read`

#### GCP IAM Roles
- Secret Manager: `roles/secretmanager.secretAccessor`
- Artifact Registry: `roles/artifactregistry.reader`
- Cloud Run: `roles/run.viewer`, `roles/run.developer`

## Error Patterns to Avoid

### ❌ Don't Use .Result on Output<T>
```csharp
// ❌ This will block and may deadlock
string arn = _resource.Arn.Apply(x => x).Result;
```

### ❌ Don't Access Private Fields from Outside Class
```csharp
// ❌ Can't access _service from outside
((AwsAppRunnerCompute)compute)._service.ServiceUrl;

// ✅ Use public properties instead
((AwsAppRunnerCompute)compute).ServiceUrl;
```

### ❌ Don't Mix Output<string> and string
```csharp
// ❌ Type mismatch
Resources = new string[] { Arn } // Arn is Output<string>

// ✅ Use consistent types
Resources = new Output<string>[] { Arn }
```

## Testing Patterns

### Unit Tests
Focus on policy generation and interface compliance:
```csharp
[Fact]
public void AwsSecret_GetPolicy_ReturnsCorrectActions()
{
    var secret = new AwsSecret("test", "value");
    var policies = secret.GetPolicy(SecretPermissions.Read);
    
    Assert.Single(policies);
    Assert.Contains("secretsmanager:GetSecretValue", policies.First().Actions);
}
```

## Extension Patterns

### Helper Extensions
```csharp
public static class CloudResourceExtensions
{
    public static T WithSecrets<T>(this T compute, params ISecret[] secrets) where T : ICompute
    {
        // Add secrets to compute resource
        return compute;
    }
}
```

## Documentation Style

- Use XML documentation for public APIs
- Include usage examples in class comments
- Document Output<T> vs string property differences
- Explain cloud-specific limitations in implementations

## When Adding New Cloud Providers

1. Create new project: `PulumiCrossCloud.{Provider}`
2. Implement all core interfaces
3. Create provider-specific policy statement class
4. Add to factory pattern
5. Create sample project
6. Update documentation

## When Adding New Resource Types

1. Define permission enum in Core
2. Create interface in Core
3. Implement in each cloud provider
4. Add to factory methods
5. Create extension methods if needed
6. Add unit tests

Remember: The goal is to make cross-cloud infrastructure as easy as switching a factory parameter!