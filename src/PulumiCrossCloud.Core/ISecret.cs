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

    /// <summary>
    /// The primary provider resource representing the secret.
    /// </summary>
    Pulumi.Resource Resource { get; }

    /// <summary>
    /// The resource whose completion makes the current secret value ready for consumers.
    /// Providers that store values in the primary secret resource can use the default.
    /// </summary>
    Pulumi.Resource ValueResource => Resource;
}
