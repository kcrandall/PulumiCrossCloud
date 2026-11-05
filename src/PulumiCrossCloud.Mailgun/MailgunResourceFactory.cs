using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Mailgun;

public class MailgunResourceFactory : CloudResourceFactory
{
    private readonly Pulumi.Mailgun.Provider? _providerResource;
    private readonly Input<string>? _apiKey;

    public MailgunResourceFactory()
        : this((Pulumi.Mailgun.Provider?)null) { }

    public MailgunResourceFactory(string? apiKey)
        : base(CloudProvider.Mailgun)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            _apiKey = Output.CreateSecret(apiKey);
            _providerResource = new Pulumi.Mailgun.Provider(
                "mailgun-provider",
                new Pulumi.Mailgun.ProviderArgs { ApiKey = apiKey }
            );
        }
    }

    public MailgunResourceFactory(Input<string> apiKey)
        : base(CloudProvider.Mailgun)
    {
        _apiKey = apiKey;
        _providerResource = new Pulumi.Mailgun.Provider(
            "mailgun-provider",
            new Pulumi.Mailgun.ProviderArgs { ApiKey = apiKey }
        );
    }

    public MailgunResourceFactory(Pulumi.Mailgun.Provider? provider)
        : base(CloudProvider.Mailgun)
    {
        _providerResource = provider;
    }

    /// <summary>
    /// Wait during deployment until Mailgun activates the domain. Defaults to true when a DNS zone
    /// is supplied. Verification uses the factory's API key, or the <c>mailgun:apiKey</c> secret.
    /// </summary>
    public bool? WaitForVerification { get; init; }

    /// <summary>
    /// Options applied by <see cref="CreateEmailService(string, string, IDnsZone?)"/>.
    /// </summary>
    public MailgunEmailServiceOptions EmailServiceOptions { get; init; } = new();

    public override IEmailService CreateEmailService(
        string name,
        string domainName,
        IDnsZone? zone = null
    ) => CreateEmailService(name, domainName, zone, EmailServiceOptions);

    public override IEmailService CreateEmailService(
        string name,
        string domainName,
        IDnsZone? zone,
        InboundMailOptions inboundMail
    ) =>
        CreateEmailService(
            name,
            domainName,
            zone,
            EmailServiceOptions with
            {
                InboundMail = inboundMail,
            }
        );

    public MailgunEmailService CreateEmailService(
        string name,
        string domainName,
        IDnsZone? zone,
        MailgunEmailServiceOptions options
    ) =>
        new(name, domainName, zone, _providerResource, _apiKey, WaitForVerification, options);
}
