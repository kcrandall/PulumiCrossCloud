namespace PulumiCrossCloud.Plugin.PostHog;

/// <summary>
/// Pulumi package schema; the C# SDK in PulumiCrossCloud.PostHog is hand-written against it.
/// </summary>
internal static class PostHogSchema
{
    public const string Json = """
        {
          "name": "crosscloud-posthog",
          "displayName": "PostHog organizations (PulumiCrossCloud)",
          "description": "Manage PostHog organizations, which the Terraform-bridged posthog provider does not.",
          "publisher": "PulumiCrossCloud",
          "repository": "https://github.com/kcrandall/PulumiCrossCloud",
          "pluginDownloadURL": "github://api.github.com/kcrandall/PulumiCrossCloud",
          "config": {
            "variables": {
              "apiKey": { "type": "string", "secret": true, "description": "PostHog personal API key. Defaults to POSTHOG_API_KEY." },
              "host": { "type": "string", "description": "PostHog URL. Defaults to POSTHOG_HOST, then https://us.posthog.com." }
            }
          },
          "provider": {
            "inputProperties": {
              "apiKey": { "type": "string", "secret": true },
              "host": { "type": "string" }
            }
          },
          "resources": {
            "crosscloud-posthog:index:Organization": {
              "description": "A PostHog organization. Creation adopts an existing organization with the same name.",
              "inputProperties": {
                "name": { "type": "string" },
                "deleteOnDestroy": { "type": "boolean", "description": "Delete the organization when the resource is destroyed. Defaults to false." }
              },
              "requiredInputs": ["name"],
              "properties": {
                "name": { "type": "string" },
                "slug": { "type": "string" },
                "deleteOnDestroy": { "type": "boolean" },
                "adopted": { "type": "boolean", "description": "Whether creation adopted an existing organization." }
              },
              "required": ["name", "deleteOnDestroy", "adopted"]
            }
          }
        }
        """;
}
