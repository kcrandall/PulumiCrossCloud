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
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var jsonPayload = SerializePayload(name, permissionGroupIds, resources);

        var commands = GetCommandScripts(OperatingSystem.IsWindows());

        var tokenResource = new Command(
            $"{name}-create",
            new CommandArgs
            {
                Create = commands.Create,
                Delete = commands.Delete,
                Interpreter = commands.Interpreter,
                Environment =
                {
                    ["CLOUDFLARE_ACCOUNT_ID"] = accountId,
                    ["CLOUDFLARE_PRIMARY_TOKEN"] = Output.CreateSecret(
                        (Output<string>)primaryToken
                    ),
                    ["CLOUDFLARE_TOKEN_PAYLOAD"] = jsonPayload,
                },
                AddPreviousOutputInEnv = true,
                Logging = Logging.None,
            },
            new CustomResourceOptions
            {
                Parent = this,
                DeleteBeforeReplace = true,
                AdditionalSecretOutputs = ["stdout", "stderr"],
            }
        );

        var result = tokenResource.Stdout.Apply(stdout =>
        {
            try
            {
                using var document = JsonDocument.Parse(stdout);
                var root = document.RootElement;
                if (!root.GetProperty("success").GetBoolean())
                    throw new InvalidOperationException(
                        "Cloudflare token creation was unsuccessful."
                    );

                var token = root.GetProperty("result");
                var id = token.GetProperty("id").GetString();
                var value = token.GetProperty("value").GetString();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException("Cloudflare returned an empty token.");

                return (Id: id, Value: value);
            }
            catch (Exception exception)
                when (exception
                        is JsonException
                            or InvalidOperationException
                            or KeyNotFoundException
                )
            {
                // The response includes the newly created bearer token. Never copy it into
                // exception messages, which Pulumi may render as unencrypted diagnostics.
                throw new InvalidOperationException(
                    "Cloudflare did not return a valid successful token response."
                );
            }
        });

        TokenId = result.Apply(token => token.Id);
        TokenValue = Output.CreateSecret(result.Apply(token => token.Value));
    }

    internal static (string Create, string Delete, string[] Interpreter) GetCommandScripts(
        bool windows
    )
    {
        // Both shells receive payloads and credentials as environment data. PowerShell 5's
        // native argument quoting cannot preserve arbitrary JSON, so send UTF-8 JSON on stdin.
        if (windows)
            return (
                """
                $ErrorActionPreference = 'Stop'
                $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
                $env:CLOUDFLARE_TOKEN_PAYLOAD | & curl.exe -sS --fail-with-body -X POST "https://api.cloudflare.com/client/v4/accounts/$env:CLOUDFLARE_ACCOUNT_ID/tokens" -H "Authorization: Bearer $env:CLOUDFLARE_PRIMARY_TOKEN" -H 'Content-Type: application/json' -H 'Accept: application/json' --data-binary '@-'
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                """,
                """
                $ErrorActionPreference = 'Stop'
                $token = $env:PULUMI_COMMAND_STDOUT | ConvertFrom-Json
                $id = $token.result.id
                if ($id -isnot [string] -or [string]::IsNullOrWhiteSpace($id)) { throw 'Previous token response has no token ID.' }
                $id = [uri]::EscapeDataString($id)
                & curl.exe -sS --fail-with-body -X DELETE "https://api.cloudflare.com/client/v4/accounts/$env:CLOUDFLARE_ACCOUNT_ID/tokens/$id" -H "Authorization: Bearer $env:CLOUDFLARE_PRIMARY_TOKEN" -H 'Content-Type: application/json'
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                """,
                ["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command"]
            );

        var createCommand = """
            set -eu
            curl -sS --fail-with-body -X POST "https://api.cloudflare.com/client/v4/accounts/$CLOUDFLARE_ACCOUNT_ID/tokens" \
                -H "Authorization: Bearer $CLOUDFLARE_PRIMARY_TOKEN" \
                -H "Content-Type: application/json" \
                -H "Accept: application/json" \
                --data "$CLOUDFLARE_TOKEN_PAYLOAD"
            """;

        // Delete exactly the token returned by the previous create. Looking up by name can
        // revoke unrelated tokens with the same name and can hide list failures in a pipeline.
        var deleteCommand = """
            set -eu
            id=$(printf '%s' "${PULUMI_COMMAND_STDOUT:-}" | jq -er '.result.id | select(type == "string" and length > 0)')
            curl -sS --fail-with-body -X DELETE "https://api.cloudflare.com/client/v4/accounts/$CLOUDFLARE_ACCOUNT_ID/tokens/$id" \
                -H "Authorization: Bearer $CLOUDFLARE_PRIMARY_TOKEN" \
                -H "Content-Type: application/json"
            """;

        return (createCommand, deleteCommand, ["/bin/sh", "-c"]);
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
