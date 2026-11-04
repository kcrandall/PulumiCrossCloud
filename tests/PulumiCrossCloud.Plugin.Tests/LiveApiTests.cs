using System.Collections.Immutable;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Bird;
using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.Mailgun;

namespace PulumiCrossCloud.Plugin.Tests;

/// <summary>
/// Runs a fact only when the named environment variables are set, so live API tests skip
/// locally and in forks but run in CI when the repository has the secrets.
/// </summary>
public sealed class LiveApiFactAttribute : FactAttribute
{
    public LiveApiFactAttribute(params string[] variables)
    {
        var missing = variables
            .Where(variable => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
            .ToList();
        if (missing.Count > 0)
            Skip = $"Set {string.Join(", ", missing)} to run against the live API.";
    }
}

/// <summary>
/// Checks the providers against the real vendor APIs, which the unit tests only model from
/// documentation. Each test cleans up what it creates and makes no DNS changes.
/// </summary>
[Trait("Category", "LiveApi")]
public sealed class LiveApiTests
{
    /// <summary>
    /// Creates a throwaway Bird domain under BIRD_TEST_DOMAIN_SUFFIX (a domain you control),
    /// reads, updates, looks it up and deletes it.
    /// </summary>
    [LiveApiFact("BIRD_API_KEY", "BIRD_TEST_DOMAIN_SUFFIX")]
    public async Task BirdSendingDomainLifecycle()
    {
        var domain =
            $"pulumi-ci-{Guid.NewGuid():N}"[..20]
            + "."
            + Environment.GetEnvironmentVariable("BIRD_TEST_DOMAIN_SUFFIX");
        var provider = new BirdResourceProvider();
        await provider.Configure(ProviderTesting.Configure(), default);
        var urn = Props.Urn(SendingDomainResource.Type, "live");

        var created = await provider.Create(
            new CreateRequest(urn, Props.Of(("domain", domain)), TimeSpan.Zero, false),
            default
        );
        var id = created.Id!;
        try
        {
            var outputs = ProviderTesting.Map(created.Outputs);
            Assert.StartsWith("dom_", id);
            Assert.Equal(domain, PropertyValues.GetString(outputs, "domain"));
            Assert.True(outputs["dnsRecords"].TryGetArray(out var records) && records.Length > 0);

            var olds = Props.Of(("domain", domain));
            var news = Props.Of(("domain", domain), ("trackingName", ""));
            var updated = await provider.Update(
                new UpdateRequest(urn, id, outputs, news, TimeSpan.Zero, [], false, olds),
                default
            );
            Assert.Equal(
                "",
                PropertyValues.GetString(ProviderTesting.Map(updated.Outputs), "trackingName")
            );

            var found = await provider.Invoke(
                new InvokeRequest(
                    BirdResourceProvider.GetSendingDomain,
                    Props.Of(("domain", domain))
                ),
                default
            );
            Assert.Equal(
                id,
                PropertyValues.GetString(ProviderTesting.Map(found.Return), "id")
            );
        }
        finally
        {
            await provider.Delete(
                new DeleteRequest(
                    urn,
                    id,
                    created.Outputs!.ToDictionary().ToImmutableDictionary(),
                    TimeSpan.Zero,
                    Props.Of()
                ),
                default
            );
        }

        var gone = await provider.Read(
            new ReadRequest(urn, id, Props.Of(), Props.Of()),
            default
        );
        Assert.True(string.IsNullOrEmpty(gone.Id));
    }

    /// <summary>
    /// Runs one Mailgun DNS check (without waiting) for MAILGUN_TEST_DOMAIN, an existing domain.
    /// </summary>
    [LiveApiFact("MAILGUN_API_KEY", "MAILGUN_TEST_DOMAIN")]
    public async Task MailgunVerificationReadsDomainState()
    {
        var domain = Environment.GetEnvironmentVariable("MAILGUN_TEST_DOMAIN")!;
        var provider = new MailgunResourceProvider();
        await provider.Configure(ProviderTesting.Configure(), default);

        var created = await provider.Create(
            new CreateRequest(
                Props.Urn(PulumiCrossCloud.Plugin.Mailgun.DomainVerificationResource.Type, "live"),
                Props.Of(("domain", domain), ("waitForSending", false)),
                TimeSpan.Zero,
                false
            ),
            default
        );

        Assert.Contains(
            PropertyValues.GetString(ProviderTesting.Map(created.Outputs), "state"),
            new[] { "active", "unverified" }
        );
    }
}
