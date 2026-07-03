using Pulumi;

namespace PulumiCrossCloud.Core;

public interface IDatabase : ICloudResource
{
    Output<string> Host { get; }
    Output<int> Port { get; }
    Output<string> Username { get; }
    Output<string> Password { get; }
    Output<string> ConnectionString { get; }
}
