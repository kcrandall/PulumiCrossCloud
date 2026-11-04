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

    public override IEmailService CreateEmailService(
        string name,
        string domainName,
        IDnsZone? zone = null
    )
    {
        return new MailgunEmailService(
            name,
            domainName,
            zone,
            _providerResource,
            _apiKey,
            WaitForVerification
        );
    }
}
