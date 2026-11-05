using Pulumi;

namespace PulumiCrossCloud.Cloudflare;

/// <summary>
/// An account-owned Cloudflare API token, managed by the native <c>crosscloud-cloudflare</c>
/// provider because the standard cloudflare.ApiToken only creates user tokens. Changing the name
/// or permissions updates the token in place and keeps its value. Stacks created by releases that
/// used a shell command for this can move to it without rotating tokens; see
/// <c>scripts/migrate-cloudflare-account-tokens.py</c>.
/// </summary>
public class AccountApiToken : ComponentResource
{
    [Output]
    public Output<string> TokenId { get; private set; } = null!;

    [Output]
    public Output<string> TokenValue { get; private set; } = null!;

    public AccountApiToken(
        string name,
        string accountId,
        Input<string> primaryToken,
        string[] permissionGroupIds,
        ComponentResourceOptions? options = null
    )
        : this(
            name,
            accountId,
            primaryToken,
            permissionGroupIds,
            new Dictionary<string, string>
            {
                [string.Format("com.cloudflare.api.account.{0}", accountId)] = "*",
            },
            options
        ) { }

    internal AccountApiToken(
        string name,
        string accountId,
        Input<string> primaryToken,
        string[] permissionGroupIds,
        IReadOnlyDictionary<string, string> resources,
        ComponentResourceOptions? options = null
    )
        : base("crosscloud:cloudflare:AccountApiToken", name, options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(permissionGroupIds);
        ArgumentNullException.ThrowIfNull(resources);
        if (permissionGroupIds.Length == 0)
            throw new ArgumentException(
                "At least one permission group is required.",
                nameof(permissionGroupIds)
            );
        if (resources.Count == 0)
            throw new ArgumentException(
                "At least one Cloudflare resource is required.",
                nameof(resources)
            );

        var provider = new CrossCloudCloudflareProvider(
            $"{name}-provider",
            new CrossCloudCloudflareProviderArgs { ApiToken = primaryToken },
            new CustomResourceOptions { Parent = this }
        );
        Token = new CloudflareAccountToken(
            $"{name}-token",
            new CloudflareAccountTokenArgs
            {
                AccountId = accountId,
                Name = name,
                PermissionGroupIds = [.. permissionGroupIds],
                Resources = resources.ToDictionary(),
            },
            new CustomResourceOptions { Parent = this, Provider = provider }
        );

        TokenId = Token.Id;
        TokenValue = Output.CreateSecret(Token.Value);
        RegisterOutputs(new Dictionary<string, object?> { ["tokenId"] = TokenId });
    }

    /// <summary>
    /// The underlying <c>crosscloud-cloudflare:index:AccountToken</c> resource.
    /// </summary>
    public CloudflareAccountToken Token { get; }
}
