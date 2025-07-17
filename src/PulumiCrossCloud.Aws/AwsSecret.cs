using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsSecret : ISecret
{
    private readonly Pulumi.Aws.SecretsManager.Secret _secret;
    private readonly Pulumi.Aws.SecretsManager.SecretVersion _secretVersion;
    
    public AwsSecret(string name, string value)
    {
        Name = name;
        _secret = new Pulumi.Aws.SecretsManager.Secret(name, new Pulumi.Aws.SecretsManager.SecretArgs
        {
            Name = name,
            Description = $"Secret for {name}"
        });
        
        // AWS requires a separate SecretVersion to store the actual value
        _secretVersion = new Pulumi.Aws.SecretsManager.SecretVersion($"{name}-version", new Pulumi.Aws.SecretsManager.SecretVersionArgs
        {
            SecretId = _secret.Id,
            SecretString = value
        });
    }
    
    public string Name { get; }
    public Output<string> Arn => _secret.Arn;
    public CloudProvider Provider => CloudProvider.AWS;
    public Output<string> Value => _secretVersion.SecretString;
    
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
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "secretsmanager:GetSecretValue" },
                        Resources = new Output<string>[] { Arn }  // Now using Output<string>[]
                    });
                    break;
                case SecretPermissions.Write:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "secretsmanager:PutSecretValue", "secretsmanager:UpdateSecret" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case SecretPermissions.Delete:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "secretsmanager:DeleteSecret" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
                case SecretPermissions.List:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "secretsmanager:ListSecrets" },
                        Resources = new Output<string>[] { Output.Create("*") }  // Convert string to Output<string>
                    });
                    break;
                case SecretPermissions.GetMetadata:
                    statements.Add(new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = new[] { "secretsmanager:DescribeSecret" },
                        Resources = new Output<string>[] { Arn }
                    });
                    break;
            }
        }
        
        return statements;
    }
}