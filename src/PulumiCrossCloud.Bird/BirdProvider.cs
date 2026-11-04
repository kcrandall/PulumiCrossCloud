using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// An explicit <c>crosscloud-bird</c> provider. Without one, resources use the default provider
/// configured through <c>crosscloud-bird:apiKey</c> and <c>crosscloud-bird:region</c> stack config
/// (or <c>BIRD_API_KEY</c>/<c>BIRD_REGION</c>).
/// </summary>
public class BirdProvider : ProviderResource
{
    public const string Package = "crosscloud-bird";

    public BirdProvider(
        string name,
        BirdProviderArgs? args = null,
        CustomResourceOptions? options = null
    )
        : base(
            Package,
            name,
            args ?? new BirdProviderArgs(),
            CrossCloudPlugins.WithDefaults(options)
        ) { }
}

public sealed class BirdProviderArgs : ResourceArgs
{
    private Input<string>? _apiKey;

    /// <summary>
    /// Bird API key with email domain permissions. Stored as a secret.
    /// </summary>
    [Input("apiKey")]
    public Input<string>? ApiKey
    {
        get => _apiKey;
        set
        {
            if (value is null)
            {
                _apiKey = null;
                return;
            }

            _apiKey = Output.CreateSecret(value.Apply(key => key));
        }
    }

    /// <summary>
    /// <c>us1</c> or <c>eu1</c>. Defaults to the region encoded in the API key.
    /// </summary>
    [Input("region")]
    public Input<string>? Region { get; set; }
}
