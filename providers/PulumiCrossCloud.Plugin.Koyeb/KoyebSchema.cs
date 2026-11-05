namespace PulumiCrossCloud.Plugin.Koyeb;

/// <summary>
/// Pulumi package schema; the C# SDK in PulumiCrossCloud.Koyeb is hand-written against it.
/// </summary>
internal static class KoyebSchema
{
    public const string Json = """
        {
          "name": "crosscloud-koyeb",
          "displayName": "Koyeb scale-to-zero (PulumiCrossCloud)",
          "description": "Set the scale-to-zero sleep delays of a Koyeb service, which the Terraform-bridged koyeb provider cannot.",
          "publisher": "PulumiCrossCloud",
          "repository": "https://github.com/kcrandall/PulumiCrossCloud",
          "pluginDownloadURL": "github://api.github.com/kcrandall/PulumiCrossCloud",
          "config": {
            "variables": {
              "apiToken": { "type": "string", "secret": true, "description": "Koyeb API token. Defaults to KOYEB_TOKEN." }
            }
          },
          "provider": {
            "inputProperties": {
              "apiToken": { "type": "string", "secret": true }
            }
          },
          "resources": {
            "crosscloud-koyeb:index:ServiceSleep": {
              "description": "Light and deep sleep delays of a service that scales to zero. Deleting it leaves the service unchanged.",
              "inputProperties": {
                "serviceId": { "type": "string", "willReplaceOnChanges": true },
                "lightSleepDelaySeconds": { "type": "integer" },
                "deepSleepDelaySeconds": { "type": "integer" },
                "triggers": { "type": "array", "items": { "type": "string" }, "description": "Values that apply the delays again when they change, such as the service's version." }
              },
              "requiredInputs": ["serviceId"],
              "properties": {
                "serviceId": { "type": "string" },
                "lightSleepDelaySeconds": { "type": "integer" },
                "deepSleepDelaySeconds": { "type": "integer" },
                "triggers": { "type": "array", "items": { "type": "string" } },
                "deploymentId": { "type": "string" }
              },
              "required": ["serviceId", "deploymentId"]
            }
          }
        }
        """;
}
