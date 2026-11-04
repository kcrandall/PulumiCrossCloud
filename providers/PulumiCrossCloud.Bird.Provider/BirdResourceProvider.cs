using System.Collections.Immutable;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;

namespace PulumiCrossCloud.Bird.Provider;

/// <summary>
/// Pulumi resource provider for Bird (bird.com) email sending domains.
/// Configuration: <c>bird:apiKey</c> (or <c>BIRD_API_KEY</c>) and optional <c>bird:region</c>
/// (or <c>BIRD_REGION</c>), which defaults to the region encoded in the API key.
/// </summary>
internal sealed class BirdResourceProvider : Pulumi.Experimental.Provider.Provider
{
    private const string ApiKey = "apiKey";
    private const string Region = "region";

    // One client for the plugin process lifetime; the engine runs one provider instance per process.
    private readonly HttpClient _http;
    private readonly Func<string, string?> _environment;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Dictionary<string, IBirdResource> _resources;
    private BirdApiClient? _client;
    private string _region = BirdRegions.Default;

    public BirdResourceProvider(
        HttpMessageHandler? handler = null,
        Func<string, string?>? environment = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"pulumi-resource-bird/{BirdProviderVersion.Current}"
        );
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _delay = delay ?? Task.Delay;
        _resources = new()
        {
            [SendingDomainResource.Type] = new SendingDomainResource(GetClient, () => _region),
            [DomainVerificationResource.Type] = new DomainVerificationResource(GetClient, _delay),
        };
    }

    public override Task<GetSchemaResponse> GetSchema(
        GetSchemaRequest request,
        CancellationToken ct
    ) => Task.FromResult(new GetSchemaResponse { Schema = BirdSchema.Json });

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
            && string.IsNullOrEmpty(_environment("BIRD_API_KEY"))
        )
            failures.Add(
                new CheckFailure(
                    ApiKey,
                    "A Bird API key is required. Run `pulumi config set --secret bird:apiKey <key>` or set BIRD_API_KEY."
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

    public override Task<DiffResponse> DiffConfig(DiffRequest request, CancellationToken ct)
    {
        var olds = request.OldInputs.IsEmpty ? request.OldOutputs : request.OldInputs;
        var news = request.NewInputs;
        var diffs = new[] { ApiKey, Region }
            .Where(key =>
                !PropertyValues.SameValue(
                    PropertyValues.Get(olds, key),
                    PropertyValues.Get(news, key)
                )
            )
            .ToList();

        // Domains live in one region, so only a change of effective region replaces resources.
        // Spelling out the region the key already implies is not a change.
        var oldRegion = EffectiveRegion(olds);
        var newRegion = EffectiveRegion(news);
        List<string> replaces =
            oldRegion is not null && newRegion is not null && oldRegion != newRegion
                ? [Region]
                : [];

        return Task.FromResult(
            new DiffResponse
            {
                Changes = diffs.Count > 0,
                Diffs = diffs,
                Replaces = replaces,
                Stables = [],
                DeleteBeforeReplace = false,
            }
        );
    }

    public override Task<ConfigureResponse> Configure(
        ConfigureRequest request,
        CancellationToken ct
    )
    {
        var apiKey = ConfigValue(request, ApiKey) ?? NonEmpty(_environment("BIRD_API_KEY"));
        var region =
            ConfigValue(request, Region)
            ?? NonEmpty(_environment("BIRD_REGION"))
            ?? BirdRegions.FromApiKey(apiKey)
            ?? BirdRegions.Default;
        if (!BirdRegions.IsSupported(region))
            throw new ArgumentException($"Unsupported Bird region '{region}'.");

        // BIRD_API_URL points the provider at another endpoint, such as a local test server.
        var baseUri = NonEmpty(_environment("BIRD_API_URL")) is { } url
            ? new Uri(url.EndsWith('/') ? url : $"{url}/")
            : BirdRegions.ApiBaseUri(region);

        _region = region;
        // The key is unknown during previews that derive it from other resources; previews make no API calls.
        _client = apiKey is null ? null : new BirdApiClient(_http, apiKey, baseUri, _delay);

        return Task.FromResult(
            new ConfigureResponse
            {
                AcceptSecrets = true,
                SupportsPreview = true,
                AcceptResources = false,
                AcceptOutputs = false,
            }
        );
    }

    public override Task<CheckResponse> Check(CheckRequest request, CancellationToken ct) =>
        Resource(request.Type).Check(request, ct);

    public override Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Resource(request.Type).Diff(request, ct);

    public override Task<CreateResponse> Create(CreateRequest request, CancellationToken ct) =>
        Resource(request.Type).Create(request, ct);

    public override Task<ReadResponse> Read(ReadRequest request, CancellationToken ct) =>
        Resource(request.Type).Read(request, ct);

    public override Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct) =>
        Resource(request.Type).Update(request, ct);

    public override Task Delete(DeleteRequest request, CancellationToken ct) =>
        Resource(request.Type).Delete(request, ct);

    private IBirdResource Resource(string type) =>
        _resources.TryGetValue(type, out var resource)
            ? resource
            : throw new NotSupportedException($"Unknown Bird resource type '{type}'.");

    private BirdApiClient GetClient() =>
        _client
        ?? throw new InvalidOperationException(
            "The Bird provider has no API key. Run `pulumi config set --secret bird:apiKey <key>`, "
                + "set BIRD_API_KEY, or pass ApiKey to an explicit Bird provider."
        );

    private string? EffectiveRegion(ImmutableDictionary<string, PropertyValue> config)
    {
        if (PropertyValues.IsComputed(config, Region) || PropertyValues.IsComputed(config, ApiKey))
            return null;

        return PropertyValues.GetString(config, Region)
            ?? BirdRegions.FromApiKey(PropertyValues.GetString(config, ApiKey))
            ?? BirdRegions.Default;
    }

    private static string? ConfigValue(ConfigureRequest request, string key) =>
        NonEmpty(PropertyValues.GetString(request.Args, key));

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
