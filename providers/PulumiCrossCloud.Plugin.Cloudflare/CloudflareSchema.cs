namespace PulumiCrossCloud.Plugin.Cloudflare;

/// <summary>
/// Pulumi package schema; the C# SDK in PulumiCrossCloud.Cloudflare is hand-written against it.
/// </summary>
internal static class CloudflareSchema
{
    public const string Json = """
        {
          "name": "crosscloud-cloudflare",
          "displayName": "Cloudflare account tokens (PulumiCrossCloud)",
          "description": "Create account-owned Cloudflare API tokens, which the Terraform-bridged cloudflare provider does not.",
          "publisher": "PulumiCrossCloud",
          "repository": "https://github.com/kcrandall/PulumiCrossCloud",
          "pluginDownloadURL": "github://api.github.com/kcrandall/PulumiCrossCloud",
          "config": {
            "variables": {
              "apiToken": { "type": "string", "secret": true, "description": "Token allowed to manage the account's API tokens. Defaults to CLOUDFLARE_API_TOKEN." }
            }
          },
          "provider": {
            "inputProperties": {
              "apiToken": { "type": "string", "secret": true }
            }
          },
          "resources": {
            "crosscloud-cloudflare:index:AccountToken": {
              "description": "An account-owned API token with one allow policy. Name and policy changes keep the token's value; deleting it revokes the token.",
              "inputProperties": {
                "accountId": { "type": "string", "willReplaceOnChanges": true },
                "name": { "type": "string" },
                "permissionGroupIds": { "type": "array", "items": { "type": "string" } },
                "resources": { "type": "object", "additionalProperties": { "type": "string" }, "description": "Resource identifiers mapped to access, such as {\"com.cloudflare.api.account.<id>\": \"*\"}." }
              },
              "requiredInputs": ["accountId", "name", "permissionGroupIds", "resources"],
              "properties": {
                "accountId": { "type": "string" },
                "name": { "type": "string" },
                "permissionGroupIds": { "type": "array", "items": { "type": "string" } },
                "resources": { "type": "object", "additionalProperties": { "type": "string" } },
                "status": { "type": "string" },
                "value": { "type": "string", "secret": true }
              },
              "required": ["accountId", "name", "permissionGroupIds", "resources", "status", "value"]
            }
          }
        }
        """;
}
