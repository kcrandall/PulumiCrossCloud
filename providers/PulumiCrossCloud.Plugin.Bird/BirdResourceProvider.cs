using System.Collections.Immutable;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Bird;

/// <summary>
/// Pulumi resource provider for Bird (bird.com) email sending domains (package <c>crosscloud-bird</c>).
/// Configuration: <c>crosscloud-bird:apiKey</c> (or <c>BIRD_API_KEY</c>) and optional
/// <c>crosscloud-bird:region</c> (or <c>BIRD_REGION</c>), which defaults to the API key's region.
/// </summary>
internal sealed class BirdResourceProvider : ResourceProviderBase
{
    public const string Package = "crosscloud-bird";
    public const string GetSendingDomain = "crosscloud-bird:index:getSendingDomain";

    private const string ApiKey = "apiKey";
    private const string Region = "region";

    private readonly SendingDomainResource _sendingDomains;
    private BirdApiClient? _client;
    private string _region = BirdRegions.Default;

    public BirdResourceProvider(
        HttpMessageHandler? handler = null,
        Func<string, string?>? environment = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
        : base(Package, BirdSchema.Json, handler, environment, delay)
    {
        _sendingDomains = new SendingDomainResource(GetClient, () => _region);
        Resources[SendingDomainResource.Type] = _sendingDomains;
        Resources[DomainVerificationResource.Type] = new DomainVerificationResource(
            GetClient,
            Delay
        );
        Resources[DomainReceivingResource.Type] = new DomainReceivingResource(GetClient, Delay);
        Functions[GetSendingDomain] = FindSendingDomain;
    }

    protected override IReadOnlyList<string> ConfigKeys { get; } = [ApiKey, Region];

    public override Task<CheckResponse> CheckConfig(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];

        var region = PropertyValues.GetString(inputs, Region);
        if (region is not null && !BirdRegions.IsSupported(region))
            failures.Add(
                new CheckFailure(
                    Region,
                    $"Unsupported Bird region '{region}'. Expected one of: {string.Join(", ", BirdRegions.Supported)}."
                )
            );

        var apiKey = PropertyValues.GetString(inputs, ApiKey);
        if (
            apiKey is null
            && !PropertyValues.IsComputed(inputs, ApiKey)
            && EnvironmentValue("BIRD_API_KEY") is null
        )
            failures.Add(
                new CheckFailure(
                    ApiKey,
                    $"A Bird API key is required. Run `pulumi config set --secret {Package}:apiKey <key>` or set BIRD_API_KEY."
                )
            );

        var keyRegion = BirdRegions.FromApiKey(apiKey);
        if (region is not null && keyRegion is not null && region != keyRegion)
            failures.Add(
                new CheckFailure(
                    Region,
                    $"Region '{region}' does not match the API key, which belongs to '{keyRegion}'."
                )
            );

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    // Domains live in one region, so only a change of effective region replaces resources.
    // Spelling out the region the key already implies is not a change.
    protected override IEnumerable<string> ConfigReplaces(
        ImmutableDictionary<string, PropertyValue> olds,
        ImmutableDictionary<string, PropertyValue> news
    )
    {
        var oldRegion = EffectiveRegion(olds);
        var newRegion = EffectiveRegion(news);
        return oldRegion is not null && newRegion is not null && oldRegion != newRegion
            ? [Region]
            : [];
    }

    public override Task<ConfigureResponse> Configure(
        ConfigureRequest request,
        CancellationToken ct
    )
    {
        var apiKey = ConfigValue(request, ApiKey) ?? EnvironmentValue("BIRD_API_KEY");
        var region =
            ConfigValue(request, Region)
            ?? EnvironmentValue("BIRD_REGION")
            ?? BirdRegions.FromApiKey(apiKey)
            ?? BirdRegions.Default;
        if (!BirdRegions.IsSupported(region))
            throw new ArgumentException($"Unsupported Bird region '{region}'.");

        _region = region;
        // The key is unknown during previews that derive it from other resources; previews make no API calls.
        _client = apiKey is null
            ? null
            : new BirdApiClient(
                Http,
                apiKey,
                BaseUri("BIRD_API_URL", BirdRegions.ApiBaseUri(region)),
                Delay
            );

        return Task.FromResult(Configured());
    }

    private async Task<InvokeResponse> FindSendingDomain(
        ImmutableDictionary<string, PropertyValue> args,
        CancellationToken ct
    )
    {
        var name = PropertyValues.GetString(args, "domain");
        if (string.IsNullOrWhiteSpace(name))
            return new InvokeResponse
            {
                Failures = [new CheckFailure("domain", "domain is required.")],
            };

        var domain =
            await GetClient().FindDomainAsync(name, ct)
            ?? throw new InvalidOperationException(
                $"Bird has no sending domain named '{name}' in this workspace."
            );
        return new InvokeResponse
        {
            Return = _sendingDomains.ToOutputs(domain).Add("id", new PropertyValue(domain.Id)),
        };
    }

    private BirdApiClient GetClient() =>
        _client
        ?? throw new InvalidOperationException(
            $"The Bird provider has no API key. Run `pulumi config set --secret {Package}:apiKey <key>`, "
                + "set BIRD_API_KEY, or pass ApiKey to an explicit Bird provider."
        );

    private static string? EffectiveRegion(ImmutableDictionary<string, PropertyValue> config)
    {
        if (PropertyValues.IsComputed(config, Region) || PropertyValues.IsComputed(config, ApiKey))
            return null;

        return PropertyValues.GetString(config, Region)
            ?? BirdRegions.FromApiKey(PropertyValues.GetString(config, ApiKey))
            ?? BirdRegions.Default;
    }
}
