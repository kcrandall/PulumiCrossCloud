using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Mailgun;

public class MailgunResourceFactory : CloudResourceFactory
{
    private readonly Pulumi.Mailgun.Provider? _providerResource;

    public MailgunResourceFactory()
        : this((Pulumi.Mailgun.Provider?)null)
    {
    }

    public MailgunResourceFactory(string? apiKey)
        : base(CloudProvider.Mailgun)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            _providerResource = new Pulumi.Mailgun.Provider("mailgun-provider", new Pulumi.Mailgun.ProviderArgs
            {
                ApiKey = apiKey
            });
        }
    }

    public MailgunResourceFactory(Input<string> apiKey)
        : base(CloudProvider.Mailgun)
    {
        _providerResource = new Pulumi.Mailgun.Provider("mailgun-provider", new Pulumi.Mailgun.ProviderArgs
        {
            ApiKey = apiKey
        });
    }

    public MailgunResourceFactory(Pulumi.Mailgun.Provider? provider)
        : base(CloudProvider.Mailgun)
    {
        _providerResource = provider;
    }

    public override IEmailService CreateEmailService(string name, string domainName, IDnsZone? zone = null)
    {
        return new MailgunEmailService(name, domainName, zone, _providerResource);
    }
}
