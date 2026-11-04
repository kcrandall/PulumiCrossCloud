using System.Collections.Immutable;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Mailgun;

/// <summary>
/// Pulumi resource provider for Mailgun domain verification (package <c>crosscloud-mailgun</c>),
/// which the Terraform-bridged <c>mailgun</c> provider does not offer.
/// Configuration: <c>crosscloud-mailgun:apiKey</c> (or <c>MAILGUN_API_KEY</c>) and
/// <c>crosscloud-mailgun:region</c> (or <c>MAILGUN_REGION</c>): <c>us</c> (default) or <c>eu</c>.
/// </summary>
internal sealed class MailgunResourceProvider : ResourceProviderBase
{
    public const string Package = "crosscloud-mailgun";

    private const string ApiKey = "apiKey";
    private const string Region = "region";
    private static readonly string[] Regions = ["us", "eu"];

    private MailgunApiClient? _client;

    public MailgunResourceProvider(
        HttpMessageHandler? handler = null,
        Func<string, string?>? environment = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
        : base(Package, MailgunSchema.Json, handler, environment, delay)
    {
        Resources[DomainVerificationResource.Type] = new DomainVerificationResource(
            GetClient,
            Delay
        );
    }

    protected override IReadOnlyList<string> ConfigKeys { get; } = [ApiKey, Region];

    public override Task<CheckResponse> CheckConfig(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        if (
            PropertyValues.GetString(inputs, ApiKey) is null
            && !PropertyValues.IsComputed(inputs, ApiKey)
            && EnvironmentValue("MAILGUN_API_KEY") is null
        )
            failures.Add(
                new CheckFailure(
                    ApiKey,
                    $"A Mailgun API key is required. Run `pulumi config set --secret {Package}:apiKey <key>` or set MAILGUN_API_KEY."
                )
            );

        var region = PropertyValues.GetString(inputs, Region);
        if (region is not null && !Regions.Contains(region))
            failures.Add(
                new CheckFailure(
                    Region,
                    $"Unsupported Mailgun region '{region}'. Expected us or eu."
                )
            );

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    protected override IEnumerable<string> ConfigReplaces(
        ImmutableDictionary<string, PropertyValue> olds,
        ImmutableDictionary<string, PropertyValue> news
    ) =>
        !PropertyValues.IsComputed(news, Region)
        && (PropertyValues.GetString(olds, Region) ?? "us")
            != (PropertyValues.GetString(news, Region) ?? "us")
            ? [Region]
            : [];

    public override Task<ConfigureResponse> Configure(
        ConfigureRequest request,
        CancellationToken ct
    )
    {
        var apiKey = ConfigValue(request, ApiKey) ?? EnvironmentValue("MAILGUN_API_KEY");
        var region = ConfigValue(request, Region) ?? EnvironmentValue("MAILGUN_REGION") ?? "us";
        if (!Regions.Contains(region))
            throw new ArgumentException($"Unsupported Mailgun region '{region}'.");

        _client = apiKey is null
            ? null
            : new MailgunApiClient(
                Http,
                apiKey,
                BaseUri("MAILGUN_API_URL", MailgunApiClient.BaseUri(region)),
                Delay
            );
        return Task.FromResult(Configured());
    }

    private MailgunApiClient GetClient() =>
        _client
        ?? throw new InvalidOperationException(
            $"The Mailgun verification provider has no API key. Run `pulumi config set --secret {Package}:apiKey <key>`, "
                + "set MAILGUN_API_KEY, or pass ApiKey to an explicit provider."
        );
}
