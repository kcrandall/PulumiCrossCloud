using System.Collections.Immutable;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.PostHog;

/// <summary>
/// Pulumi resource provider for PostHog organizations (package <c>crosscloud-posthog</c>), which
/// the Terraform-bridged <c>posthog</c> provider does not manage.
/// Configuration: <c>crosscloud-posthog:apiKey</c> (or <c>POSTHOG_API_KEY</c>), a personal API key
/// with organization scopes, and <c>crosscloud-posthog:host</c> (or <c>POSTHOG_HOST</c>), which
/// defaults to https://us.posthog.com.
/// </summary>
internal sealed class PostHogResourceProvider : ResourceProviderBase
{
    public const string Package = "crosscloud-posthog";
    public const string DefaultHost = "https://us.posthog.com";

    private const string ApiKey = "apiKey";
    private const string Host = "host";

    private PostHogApiClient? _client;

    public PostHogResourceProvider(
        HttpMessageHandler? handler = null,
        Func<string, string?>? environment = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
        : base(Package, PostHogSchema.Json, handler, environment, delay)
    {
        Resources[OrganizationResource.Type] = new OrganizationResource(GetClient);
    }

    protected override IReadOnlyList<string> ConfigKeys { get; } = [ApiKey, Host];

    public override Task<CheckResponse> CheckConfig(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        if (
            PropertyValues.GetString(inputs, ApiKey) is null
            && !PropertyValues.IsComputed(inputs, ApiKey)
            && EnvironmentValue("POSTHOG_API_KEY") is null
        )
            failures.Add(
                new CheckFailure(
                    ApiKey,
                    $"A PostHog personal API key is required. Run `pulumi config set --secret {Package}:apiKey <key>` or set POSTHOG_API_KEY."
                )
            );

        var host = PropertyValues.GetString(inputs, Host);
        if (
            host is not null
            && (
                !Uri.TryCreate(host, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("https" or "http")
            )
        )
            failures.Add(new CheckFailure(Host, $"'{host}' is not an absolute http(s) URL."));

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    // Organizations belong to one PostHog instance, so pointing at another host replaces them.
    protected override IEnumerable<string> ConfigReplaces(
        ImmutableDictionary<string, PropertyValue> olds,
        ImmutableDictionary<string, PropertyValue> news
    )
    {
        var oldHost = PropertyValues.GetString(olds, Host) ?? DefaultHost;
        var newHost = PropertyValues.GetString(news, Host) ?? DefaultHost;
        return
            PropertyValues.IsComputed(news, Host)
            || string.Equals(
                oldHost.TrimEnd('/'),
                newHost.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase
            )
            ? []
            : [Host];
    }

    public override Task<ConfigureResponse> Configure(
        ConfigureRequest request,
        CancellationToken ct
    )
    {
        var apiKey = ConfigValue(request, ApiKey) ?? EnvironmentValue("POSTHOG_API_KEY");
        var host = ConfigValue(request, Host) ?? EnvironmentValue("POSTHOG_HOST") ?? DefaultHost;
        _client = apiKey is null ? null : new PostHogApiClient(Http, apiKey, new Uri(host), Delay);
        return Task.FromResult(Configured());
    }

    private PostHogApiClient GetClient() =>
        _client
        ?? throw new InvalidOperationException(
            $"The PostHog provider has no API key. Run `pulumi config set --secret {Package}:apiKey <key>`, "
                + "set POSTHOG_API_KEY, or pass ApiKey to an explicit provider."
        );
}
