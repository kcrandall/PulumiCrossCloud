using Pulumi;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// An explicit <c>bird</c> provider. Without one, resources use the default provider configured
/// through <c>bird:apiKey</c> and <c>bird:region</c> stack config (or <c>BIRD_API_KEY</c>/<c>BIRD_REGION</c>).
/// </summary>
public class BirdProvider : ProviderResource
{
    public BirdProvider(
        string name,
        BirdProviderArgs? args = null,
        CustomResourceOptions? options = null
    )
        : base(
            BirdPlugin.Name,
            name,
            args ?? new BirdProviderArgs(),
            BirdPlugin.WithDefaults(options)
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
