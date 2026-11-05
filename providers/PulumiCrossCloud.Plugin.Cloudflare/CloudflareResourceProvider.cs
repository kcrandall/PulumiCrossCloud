using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Cloudflare;

/// <summary>
/// Pulumi resource provider for Cloudflare account API tokens (package
/// <c>crosscloud-cloudflare</c>), which the Terraform-bridged <c>cloudflare</c> provider does not
/// create. Configuration: <c>crosscloud-cloudflare:apiToken</c> (or <c>CLOUDFLARE_API_TOKEN</c>), a
/// token allowed to manage the account's API tokens.
/// </summary>
internal sealed class CloudflareResourceProvider : ResourceProviderBase
{
    public const string Package = "crosscloud-cloudflare";

    private const string ApiToken = "apiToken";

    private CloudflareApiClient? _client;

    public CloudflareResourceProvider(
        HttpMessageHandler? handler = null,
        Func<string, string?>? environment = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
        : base(Package, CloudflareSchema.Json, handler, environment, delay)
    {
        Resources[AccountTokenResource.Type] = new AccountTokenResource(GetClient);
    }

    protected override IReadOnlyList<string> ConfigKeys { get; } = [ApiToken];

    public override Task<CheckResponse> CheckConfig(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        if (
            PropertyValues.GetString(inputs, ApiToken) is null
            && !PropertyValues.IsComputed(inputs, ApiToken)
            && EnvironmentValue("CLOUDFLARE_API_TOKEN") is null
        )
            failures.Add(
                new CheckFailure(
                    ApiToken,
                    $"A Cloudflare API token is required. Run `pulumi config set --secret {Package}:apiToken <token>` or set CLOUDFLARE_API_TOKEN."
                )
            );

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    public override Task<ConfigureResponse> Configure(
        ConfigureRequest request,
        CancellationToken ct
    )
    {
        var apiToken = ConfigValue(request, ApiToken) ?? EnvironmentValue("CLOUDFLARE_API_TOKEN");
        _client = apiToken is null
            ? null
            : new CloudflareApiClient(
                Http,
                apiToken,
                BaseUri("CLOUDFLARE_API_URL", CloudflareApiClient.DefaultBaseUri),
                Delay
            );
        return Task.FromResult(Configured());
    }

    private CloudflareApiClient GetClient() =>
        _client
        ?? throw new InvalidOperationException(
            $"The Cloudflare provider has no API token. Run `pulumi config set --secret {Package}:apiToken <token>`, "
                + "set CLOUDFLARE_API_TOKEN, or pass ApiToken to an explicit provider."
        );
}
