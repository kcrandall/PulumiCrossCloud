using Pulumi;

namespace PulumiCrossCloud.Core;

public interface ICloudResource
{
    string Name { get; }
    Output<string> Arn { get; }
    CloudProvider Provider { get; }
}
