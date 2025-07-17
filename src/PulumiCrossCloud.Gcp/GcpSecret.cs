using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpSecret : ISecret
{
    private readonly Pulumi.Gcp.SecretManager.Secret _secret;
    
    public GcpSecret(string name, string value)
    {
        Name = name;
        var projectId = GcpProjectConfig.ProjectId;
    
        _secret = new Pulumi.Gcp.SecretManager.Secret(name, new Pulumi.Gcp.SecretManager.SecretArgs
        {
            SecretId = name,
            Project = projectId
        });
    
        // GCP separates secret and secret version
        new Pulumi.Gcp.SecretManager.SecretVersion($"{name}-version", new Pulumi.Gcp.SecretManager.SecretVersionArgs
        {
            Secret = _secret.Id,
            SecretData = value
        });
    }
    
    public string Name { get; }
    public Output<string> Arn => _secret.Name; // GCP uses resource names instead of ARNs
    public CloudProvider Provider => CloudProvider.GCP;
    public Output<string> Value => _secret.Name; // Would need to get version value
    
    public async Task<string> GetValueAsync()
    {
        throw new NotImplementedException("Secret values are resolved during Pulumi deployment. Use the Value Output<string> property instead.");
    }
    
    public void SetValue(string value) 
    { 
        throw new NotImplementedException("Use Pulumi resource updates to change secret values"); 
    }
    
    public IEnumerable<IPolicyStatement> GetPolicy(params SecretPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();
        
        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case SecretPermissions.Read:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/secretmanager.secretAccessor" },
                        Resources = new Output<string>[] { Arn }  // Fixed to use Output<string>[]
                    });
                    break;
                case SecretPermissions.Write:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/secretmanager.secretVersionAdder" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case SecretPermissions.List:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/secretmanager.viewer" },
                        Resources = new Output<string>[] { Arn.Apply(arn => arn.Replace($"/secrets/{Name}", "/secrets/*")) }
                    });
                    break;
                case SecretPermissions.Delete:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/secretmanager.admin" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case SecretPermissions.GetMetadata:
                    statements.Add(new GcpPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "roles/secretmanager.viewer" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
            }
        }
        
        return statements;
    }
}