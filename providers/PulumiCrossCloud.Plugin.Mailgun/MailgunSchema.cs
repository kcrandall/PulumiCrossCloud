namespace PulumiCrossCloud.Plugin.Mailgun;

/// <summary>
/// Pulumi package schema; the C# SDK in PulumiCrossCloud.Mailgun is hand-written against it.
/// </summary>
internal static class MailgunSchema
{
    public const string Json = """
        {
          "name": "crosscloud-mailgun",
          "displayName": "Mailgun verification (PulumiCrossCloud)",
          "description": "Verify Mailgun sending domains, which the Terraform-bridged mailgun provider does not.",
          "publisher": "PulumiCrossCloud",
          "repository": "https://github.com/kcrandall/PulumiCrossCloud",
          "pluginDownloadURL": "github://api.github.com/kcrandall/PulumiCrossCloud",
          "config": {
            "variables": {
              "apiKey": { "type": "string", "secret": true, "description": "Mailgun API key. Defaults to MAILGUN_API_KEY." },
              "region": { "type": "string", "description": "us (default) or eu. Defaults to MAILGUN_REGION." }
            }
          },
          "provider": {
            "inputProperties": {
              "apiKey": { "type": "string", "secret": true },
              "region": { "type": "string" }
            }
          },
          "resources": {
            "crosscloud-mailgun:index:DomainVerification": {
              "description": "Runs Mailgun's DNS check for a domain and waits until it is active.",
              "inputProperties": {
                "domain": { "type": "string", "willReplaceOnChanges": true },
                "waitForSending": { "type": "boolean", "description": "Wait until the domain is active. Defaults to true." },
                "triggers": { "type": "array", "items": { "type": "string" }, "willReplaceOnChanges": true }
              },
              "requiredInputs": ["domain"],
              "properties": {
                "domain": { "type": "string" },
                "waitForSending": { "type": "boolean" },
                "triggers": { "type": "array", "items": { "type": "string" } },
                "state": { "type": "string" }
              },
              "required": ["domain", "waitForSending", "state"]
            }
          }
        }
        """;
}
