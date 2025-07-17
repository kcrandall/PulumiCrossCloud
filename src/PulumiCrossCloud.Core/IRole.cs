namespace PulumiCrossCloud.Core;

public interface IRole : ICloudResource
{
    void AttachPolicies(IEnumerable<IPolicyStatement> policies);
    void AttachInlinePolicy(string policyName, IEnumerable<IPolicyStatement> statements);
}