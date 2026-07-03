using Pulumi;

namespace PulumiCrossCloud.Core;

public interface IPolicyStatement
{
    string Effect { get; }
    IEnumerable<string> Actions { get; }
    IEnumerable<Output<string>> Resources { get; }
    /// <summary>
    /// Optional stable names for the resources, used for deterministic Pulumi resource naming.
    /// </summary>
    IEnumerable<string> ResourceNames { get; }
    IDictionary<string, object> Conditions { get; }
}

public abstract class BasePolicyStatement : IPolicyStatement
{
    public string Effect { get; set; } = "Allow";
    public IEnumerable<string> Actions { get; set; } = new List<string>();
    public IEnumerable<Output<string>> Resources { get; set; } = new List<Output<string>>();
    public IEnumerable<string> ResourceNames { get; set; } = new List<string>();
    public IDictionary<string, object> Conditions { get; set; } = new Dictionary<string, object>();

    public abstract string ToNativeFormat();
}
