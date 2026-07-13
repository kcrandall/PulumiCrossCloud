using System.Text.Json;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpPolicyStatement : BasePolicyStatement
{
    public IEnumerable<string> Members { get; set; } = new List<string>();

    public override string ToNativeFormat()
    {
        var binding = new
        {
            role = string.Join(",", Actions),
            members = Members,
            condition = Conditions.Count > 0
                ? new
                {
                    expression = Conditions.ContainsKey("expression")
                        ? Conditions["expression"]
                        : "",
                    title = Conditions.ContainsKey("title") ? Conditions["title"] : "",
                    description = Conditions.ContainsKey("description")
                        ? Conditions["description"]
                        : "",
                }
                : null,
        };

        return JsonSerializer.Serialize(
            binding,
            new JsonSerializerOptions { WriteIndented = true }
        );
    }
}
