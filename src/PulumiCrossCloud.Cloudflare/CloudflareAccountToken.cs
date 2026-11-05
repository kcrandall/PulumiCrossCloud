using System.Collections.Immutable;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Cloudflare;

/// <summary>
/// An account-owned Cloudflare API token with one allow policy
/// (<c>crosscloud-cloudflare:index:AccountToken</c>). Name and policy changes update it in place
/// and keep its value; changing the account replaces it, and deleting it revokes the token.
/// Served by the native <c>crosscloud-cloudflare</c> provider, because the Terraform-bridged
/// <c>cloudflare</c> provider only creates user tokens. Prefer <see cref="AccountApiToken"/>,
/// which also creates the provider.
/// </summary>
public class CloudflareAccountToken : CustomResource
{
    public const string TypeToken = "crosscloud-cloudflare:index:AccountToken";

    [Output("accountId")]
    public Output<string> AccountId { get; private set; } = null!;

    [Output("name")]
    public Output<string> Name { get; private set; } = null!;

    [Output("permissionGroupIds")]
    public Output<ImmutableArray<string>> PermissionGroupIds { get; private set; } = null!;

    [Output("resources")]
    public Output<ImmutableDictionary<string, string>> Resources { get; private set; } = null!;

    /// <summary>
    /// active, disabled or expired.
    /// </summary>
    [Output("status")]
    public Output<string> Status { get; private set; } = null!;

    /// <summary>
    /// The bearer token. Cloudflare only returns it when the token is created.
    /// </summary>
    [Output("value")]
    public Output<string> Value { get; private set; } = null!;

    public CloudflareAccountToken(
        string name,
        CloudflareAccountTokenArgs args,
        CustomResourceOptions? options = null
    )
        : base(
            TypeToken,
            name,
            args,
            CustomResourceOptions.Merge(
                CrossCloudPlugins.WithDefaults(options),
                new CustomResourceOptions { AdditionalSecretOutputs = ["value"] }
            )
        ) { }
}

public sealed class CloudflareAccountTokenArgs : ResourceArgs
{
    [Input("accountId", required: true)]
    public Input<string> AccountId { get; set; } = null!;

    [Input("name", required: true)]
    public Input<string> Name { get; set; } = null!;

    private InputList<string>? _permissionGroupIds;

    [Input("permissionGroupIds", required: true)]
    public InputList<string> PermissionGroupIds
    {
        get => _permissionGroupIds ??= [];
        set => _permissionGroupIds = value;
    }

    private InputMap<string>? _resources;

    /// <summary>
    /// Resource identifiers mapped to access, such as
    /// <c>com.cloudflare.api.account.&lt;id&gt;</c> → <c>*</c>.
    /// </summary>
    [Input("resources", required: true)]
    public InputMap<string> Resources
    {
        get => _resources ??= [];
        set => _resources = value;
    }
}

/// <summary>
/// An explicit <c>crosscloud-cloudflare</c> provider. Without one, resources use the default
/// provider configured by <c>crosscloud-cloudflare:apiToken</c> stack config (or
/// <c>CLOUDFLARE_API_TOKEN</c>).
/// </summary>
public class CrossCloudCloudflareProvider : ProviderResource
{
    public const string Package = "crosscloud-cloudflare";

    public CrossCloudCloudflareProvider(
        string name,
        CrossCloudCloudflareProviderArgs? args = null,
        CustomResourceOptions? options = null
    )
        : base(
            Package,
            name,
            args ?? new CrossCloudCloudflareProviderArgs(),
            CrossCloudPlugins.WithDefaults(options)
        ) { }
}

public sealed class CrossCloudCloudflareProviderArgs : ResourceArgs
{
    private Input<string>? _apiToken;

    /// <summary>
    /// A token allowed to manage the account's API tokens. Stored as a secret.
    /// </summary>
    [Input("apiToken")]
    public Input<string>? ApiToken
    {
        get => _apiToken;
        set
        {
            if (value is null)
            {
                _apiToken = null;
                return;
            }

            _apiToken = Output.CreateSecret(value.Apply(token => token));
        }
    }
}
