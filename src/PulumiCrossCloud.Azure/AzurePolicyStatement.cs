using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzurePolicyStatement : IPolicyStatement
{
    public string Effect { get; set; } = "Allow";
    public IEnumerable<string> Actions { get; set; } = [];
    public IEnumerable<string> NotActions { get; set; } = [];
    public IEnumerable<string> DataActions { get; set; } = [];
    public IEnumerable<string> NotDataActions { get; set; } = [];
    public IEnumerable<Output<string>> Resources { get; set; } = [];
    public IEnumerable<string> ResourceNames { get; set; } = [];
    public IDictionary<string, object> Conditions { get; set; } = new Dictionary<string, object>();

    public string ToNativeFormat()
    {
        // Azure doesn't use a single JSON policy document like AWS.
        // This is usually used to create RoleDefinitions or RoleAssignments.
        return string.Join(",", Actions);
    }
}
