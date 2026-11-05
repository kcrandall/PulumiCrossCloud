using PulumiCrossCloud.Plugin.Common;
using Pulumi.Experimental.Provider;

namespace PulumiCrossCloud.Plugin.Neon;

/// <summary>
/// Pulumi resource provider for Neon compute settings (package <c>crosscloud-neon</c>), which the
/// Terraform-bridged <c>neon</c> provider cannot update on an existing compute.
/// Configuration: <c>crosscloud-neon:apiKey</c> (or <c>NEON_API_KEY</c>).
/// </summary>
internal sealed class NeonResourceProvider : ResourceProviderBase
{
    public const string Package = "crosscloud-neon";

    private const string ApiKey = "apiKey";

    private NeonApiClient? _client;

    public NeonResourceProvider(
        HttpMessageHandler? handler = null,
        Func<string, string?>? environment = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
        : base(Package, NeonSchema.Json, handler, environment, delay)
    {
        Resources[EndpointSettingsResource.Type] = new EndpointSettingsResource(GetClient, Delay);
    }

    protected override IReadOnlyList<string> ConfigKeys { get; } = [ApiKey];

    public override Task<CheckResponse> CheckConfig(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        if (
            PropertyValues.GetString(inputs, ApiKey) is null
            && !PropertyValues.IsComputed(inputs, ApiKey)
            && EnvironmentValue("NEON_API_KEY") is null
        )
            failures.Add(
                new CheckFailure(
                    ApiKey,
                    $"A Neon API key is required. Run `pulumi config set --secret {Package}:apiKey <key>` or set NEON_API_KEY."
                )
            );

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    public override Task<ConfigureResponse> Configure(
        ConfigureRequest request,
        CancellationToken ct
    )
    {
        var apiKey = ConfigValue(request, ApiKey) ?? EnvironmentValue("NEON_API_KEY");
        _client = apiKey is null
            ? null
            : new NeonApiClient(
                Http,
                apiKey,
                BaseUri("NEON_API_URL", NeonApiClient.DefaultBaseUri),
                Delay
            );
        return Task.FromResult(Configured());
    }

    private NeonApiClient GetClient() =>
        _client
        ?? throw new InvalidOperationException(
            $"The Neon provider has no API key. Run `pulumi config set --secret {Package}:apiKey <key>`, "
                + "set NEON_API_KEY, or pass ApiKey to an explicit provider."
        );
}
