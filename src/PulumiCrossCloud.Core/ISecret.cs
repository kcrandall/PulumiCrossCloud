using Pulumi;

namespace PulumiCrossCloud.Core;

public interface ISecret : ICloudResource, IPolicyProvider<SecretPermissions>
{
    Output<string> Value { get; }
    Task<string> GetValueAsync();
    void SetValue(string value);
}