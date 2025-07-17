using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzureRole : IRole
{
    public string Name { get; private set; }
    public Output<string> Arn { get; private set; }  // Changed to Output<string>
    public CloudProvider Provider => CloudProvider.Azure;
    
    public AzureRole(string name, string roleDefinition)
    {
        Name = name;
        // Azure role definition implementation would go here
        Arn = Output.Create($"/subscriptions/sub/providers/Microsoft.Authorization/roleDefinitions/{Guid.NewGuid()}");
    }
    
    public void AttachPolicies(IEnumerable<IPolicyStatement> policies) 
    { 
        throw new NotImplementedException("Azure role policies implementation needed"); 
    }
    
    public void AttachInlinePolicy(string policyName, IEnumerable<IPolicyStatement> statements) 
    { 
        // In Azure, you would typically create a custom role definition with these permissions
        // For now, this is a placeholder
        throw new NotImplementedException("Azure role policies implementation needed");
    }
}