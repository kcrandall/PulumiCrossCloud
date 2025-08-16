using Pulumi;

namespace PulumiCrossCloud.Core;

public interface ISecret : ICloudResource, IPolicyProvider<SecretPermissions>
{
    /// <summary>
    /// The name of the secret.
    /// </summary>
    Output<string?> Value { get; }
    Task<string> GetValueAsync();
    void SetValue(string value);

    /// <summary>
    /// If true, the secret value will only be set on creation and ignored on future updates.
    /// </summary>
    bool IgnoreChangesAfterCreate { get; }

    Pulumi.Resource Resource { get; }
}
