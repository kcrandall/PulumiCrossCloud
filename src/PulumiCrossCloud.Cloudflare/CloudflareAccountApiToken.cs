using System.Text.Json;
using Pulumi;
using Pulumi.Command.Local;

namespace PulumiCrossCloud.Cloudflare;

/// <summary>
/// Creates a Cloudflare API Token at the Account level using the Cloudflare API.
/// This is used because the standard cloudflare.ApiToken only supports User-level tokens.
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
        var jsonPayload = SerializePayload(name, permissionGroupIds, resources);

        // We use a local command to call the Cloudflare API via curl
        // This is an escape hatch since the Pulumi provider doesn't support Account-level tokens yet.
        var escapedPayload = jsonPayload.Replace("\"", "\\\"");
        // Be explicit about curl behavior:
        // -sS: silent but show errors; --fail-with-body: non-2xx exits non-zero and keeps body for diagnostics
        // Accept header helps ensure JSON payloads
        var createCommand = Output
            .Tuple<string, string>(primaryToken, escapedPayload)
            .Apply(token =>
                string.Format(
                    "curl -sS --fail-with-body -X POST \"https://api.cloudflare.com/client/v4/accounts/{0}/tokens\" "
                        + "-H \"Authorization: Bearer {1}\" "
                        + "-H \"Content-Type: application/json\" "
                        + "-H \"Accept: application/json\" "
                        + "--data \"{2}\"",
                    accountId,
                    token.Item1,
                    token.Item2
                )
            );

        var deleteCommand = Output
            .Tuple<string, string>(primaryToken, name)
            .Apply(token =>
                string.Format(
                    "curl -sS --fail-with-body -X GET \"https://api.cloudflare.com/client/v4/accounts/{0}/tokens\" "
                        + "-H \"Authorization: Bearer {1}\" "
                        + "-H \"Content-Type: application/json\" | "
                        + "jq -r '.result[] | select(.name == \"{2}\") | .id' | "
                        + "while read id; do "
                        + "  if [ ! -z \"$id\" ]; then "
                        + "    curl -sS --fail-with-body -X DELETE \"https://api.cloudflare.com/client/v4/accounts/{0}/tokens/$id\" "
                        + "    -H \"Authorization: Bearer {1}\" "
                        + "    -H \"Content-Type: application/json\"; "
                        + "  fi; "
                        + "done",
                    accountId,
                    token.Item1,
                    token.Item2
                )
            );

        var tokenResource = new Command(
            $"{name}-create",
            new CommandArgs { Create = createCommand, Delete = deleteCommand },
            new CustomResourceOptions { Parent = this }
        );

        // Parse the JSON output from curl
        // Capture both stdout and stderr so we can surface useful diagnostics
        var result = Output
            .Tuple(tokenResource.Stdout, tokenResource.Stderr)
            .Apply(outputs =>
            {
                var stdout = outputs.Item1 ?? string.Empty;
                var stderr = outputs.Item2 ?? string.Empty;
                try
                {
                    if (string.IsNullOrWhiteSpace(stdout))
                    {
                        var hint = string.IsNullOrWhiteSpace(stderr)
                            ? ""
                            : string.Format(" Stderr: {0}", stderr.Trim());
                        throw new Exception(
                            string.Format("Empty response from Cloudflare API.{0}", hint)
                        );
                    }

                    var doc = JsonDocument.Parse(stdout);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
                    {
                        var message = stdout;
                        if (!string.IsNullOrWhiteSpace(stderr))
                            message += string.Format("\nStderr: {0}", stderr.Trim());
                        throw new Exception(string.Format("Cloudflare API error: {0}", message));
                    }
                    var res = root.GetProperty("result");
                    return new
                    {
                        id = res.GetProperty("id").GetString()!,
                        value = res.GetProperty("value").GetString()!,
                    };
                }
                catch (Exception ex)
                {
                    var message = stdout;
                    if (!string.IsNullOrWhiteSpace(stderr))
                        message += string.Format("\nStderr: {0}", stderr.Trim());
                    throw new Exception(
                        string.Format(
                            "Failed to parse Cloudflare token response: {0}. Output was: {1}",
                            ex.Message,
                            message
                        )
                    );
                }
            });

        TokenId = result.Apply(r => r.id);
        TokenValue = result.Apply(r => r.value);
    }

    internal static string SerializePayload(
        string name,
        IReadOnlyCollection<string> permissionGroupIds,
        IReadOnlyDictionary<string, string> resources
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(permissionGroupIds);
        ArgumentNullException.ThrowIfNull(resources);

        if (permissionGroupIds.Count == 0)
            throw new ArgumentException(
                "At least one permission group is required.",
                nameof(permissionGroupIds)
            );

        if (resources.Count == 0)
            throw new ArgumentException(
                "At least one Cloudflare resource is required.",
                nameof(resources)
            );

        var payload = new
        {
            name,
            policies = new[]
            {
                new
                {
                    effect = "allow",
                    resources,
                    permission_groups = permissionGroupIds.Select(id => new { id }).ToArray(),
                },
            },
        };

        return JsonSerializer.Serialize(payload);
    }
}
