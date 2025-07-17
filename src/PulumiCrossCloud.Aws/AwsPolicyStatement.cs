using System.Text.Json;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsPolicyStatement : BasePolicyStatement
{
    public override string ToNativeFormat()
    {
        var statement = new
        {
            Effect = Effect,
            Action = Actions,
            Resource = Resources,
            Condition = Conditions.Count > 0 ? Conditions : null
        };
        
        return JsonSerializer.Serialize(statement, new JsonSerializerOptions { WriteIndented = true });
    }
}