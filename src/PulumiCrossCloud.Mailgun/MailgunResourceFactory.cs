using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Mailgun;

public class MailgunResourceFactory : CloudResourceFactory
{
    private readonly Pulumi.Mailgun.Provider? _providerResource;

    public MailgunResourceFactory(string? apiKey = null) : base(CloudProvider.Mailgun)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            _providerResource = new Pulumi.Mailgun.Provider("mailgun-provider", new Pulumi.Mailgun.ProviderArgs
            {
                ApiKey = apiKey
            });
        }
    }

    public override IEmailService CreateEmailService(string name, string domainName, IDnsZone? zone = null)
    {
        return new MailgunEmailService(name, domainName, zone, _providerResource);
    }
}
