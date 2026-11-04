using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

public class BirdResourceFactory : CloudResourceFactory
{
    private readonly BirdProvider? _providerResource;
    private readonly Input<string>? _apiKey;
    private readonly BirdRegion _region;

    /// <summary>
    /// Uses the default Bird provider, configured by the <c>bird:apiKey</c> stack config secret
    /// (or <c>BIRD_API_KEY</c>), which is also used as the SMTP password.
    /// </summary>
    public BirdResourceFactory(BirdRegion region = BirdRegion.Us1)
        : base(CloudProvider.Bird)
    {
        _region = region;
    }

    /// <summary>
    /// Creates an explicit Bird provider with <paramref name="apiKey"/>, which is also used as the SMTP password.
    /// </summary>
    public BirdResourceFactory(Input<string> apiKey, BirdRegion region = BirdRegion.Us1)
        : this(
            new BirdProvider(
                "bird-provider",
                new BirdProviderArgs { ApiKey = apiKey, Region = region.ToApiValue() }
            ),
            apiKey,
            region
        ) { }

    public BirdResourceFactory(
        BirdProvider provider,
        Input<string> apiKey,
        BirdRegion region = BirdRegion.Us1
    )
        : base(CloudProvider.Bird)
    {
        _providerResource = provider;
        _apiKey = apiKey;
        _region = region;
    }

    /// <summary>
    /// Options applied by <see cref="CreateEmailService(string, string, IDnsZone?)"/>.
    /// </summary>
    public BirdEmailServiceOptions EmailServiceOptions { get; init; } = new();

    public override IEmailService CreateEmailService(
        string name,
        string domainName,
        IDnsZone? zone = null
    ) => CreateEmailService(name, domainName, zone, EmailServiceOptions);

    public BirdEmailService CreateEmailService(
        string name,
        string domainName,
        IDnsZone? zone,
        BirdEmailServiceOptions options
    )
    {
        var smtpApiKey =
            options.SmtpApiKey ?? _apiKey ?? new Config(BirdPlugin.Name).RequireSecret("apiKey");
        return new BirdEmailService(
            name,
            domainName,
            smtpApiKey,
            _region,
            zone,
            _providerResource,
            options
        );
    }
}
