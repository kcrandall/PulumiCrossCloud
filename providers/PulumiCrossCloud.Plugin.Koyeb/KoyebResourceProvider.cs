using PulumiCrossCloud.Plugin.Common;
using Pulumi.Experimental.Provider;

namespace PulumiCrossCloud.Plugin.Koyeb;

/// <summary>
/// Pulumi resource provider for Koyeb scale-to-zero sleep delays (package <c>crosscloud-koyeb</c>), which the
/// Terraform-bridged <c>koyeb</c> provider cannot set.
/// Configuration: <c>crosscloud-koyeb:apiToken</c> (or <c>KOYEB_TOKEN</c>).
/// </summary>
internal sealed class KoyebResourceProvider : ResourceProviderBase
{
    public const string Package = "crosscloud-koyeb";

    private const string ApiToken = "apiToken";

    private KoyebApiClient? _client;

    public KoyebResourceProvider(
        HttpMessageHandler? handler = null,
        Func<string, string?>? environment = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
        : base(Package, KoyebSchema.Json, handler, environment, delay)
    {
        Resources[ServiceSleepResource.Type] = new ServiceSleepResource(GetClient, Delay);
    }

    protected override IReadOnlyList<string> ConfigKeys { get; } = [ApiToken];

    public override Task<CheckResponse> CheckConfig(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        if (
            PropertyValues.GetString(inputs, ApiToken) is null
            && !PropertyValues.IsComputed(inputs, ApiToken)
            && EnvironmentValue("KOYEB_TOKEN") is null
        )
            failures.Add(
                new CheckFailure(
                    ApiToken,
                    $"A Koyeb API token is required. Run `pulumi config set --secret {Package}:apiToken <token>` or set KOYEB_TOKEN."
                )
            );

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    public override Task<ConfigureResponse> Configure(
        ConfigureRequest request,
        CancellationToken ct
    )
    {
        var apiToken = ConfigValue(request, ApiToken) ?? EnvironmentValue("KOYEB_TOKEN");
        _client = apiToken is null
            ? null
            : new KoyebApiClient(
                Http,
                apiToken,
                BaseUri("KOYEB_API_URL", KoyebApiClient.DefaultBaseUri),
                Delay
            );
        return Task.FromResult(Configured());
    }

    private KoyebApiClient GetClient() =>
        _client
        ?? throw new InvalidOperationException(
            $"The Koyeb provider has no API token. Run `pulumi config set --secret {Package}:apiToken <token>`, "
                + "set KOYEB_TOKEN, or pass ApiToken to an explicit provider."
        );
}
