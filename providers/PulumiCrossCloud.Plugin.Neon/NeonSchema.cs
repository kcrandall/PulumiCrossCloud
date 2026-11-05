namespace PulumiCrossCloud.Plugin.Neon;

/// <summary>
/// Pulumi package schema; the C# SDK in PulumiCrossCloud.Neon is hand-written against it.
/// </summary>
internal static class NeonSchema
{
    public const string Json = """
        {
          "name": "crosscloud-neon",
          "displayName": "Neon compute settings (PulumiCrossCloud)",
          "description": "Update the autoscaling limits and suspend timeout of an existing Neon compute, which the Terraform-bridged neon provider cannot.",
          "publisher": "PulumiCrossCloud",
          "repository": "https://github.com/kcrandall/PulumiCrossCloud",
          "pluginDownloadURL": "github://api.github.com/kcrandall/PulumiCrossCloud",
          "config": {
            "variables": {
              "apiKey": { "type": "string", "secret": true, "description": "Neon API key. Defaults to NEON_API_KEY." }
            }
          },
          "provider": {
            "inputProperties": {
              "apiKey": { "type": "string", "secret": true }
            }
          },
          "resources": {
            "crosscloud-neon:index:EndpointSettings": {
              "description": "Settings of a branch's read/write compute. Unset settings are not managed; deleting the resource leaves the compute unchanged.",
              "inputProperties": {
                "projectId": { "type": "string", "willReplaceOnChanges": true },
                "branchId": { "type": "string", "willReplaceOnChanges": true },
                "minCu": { "type": "number" },
                "maxCu": { "type": "number" },
                "suspendTimeoutSeconds": { "type": "integer", "description": "-1 never suspends; 0 uses Neon's default." }
              },
              "requiredInputs": ["projectId", "branchId"],
              "properties": {
                "projectId": { "type": "string" },
                "branchId": { "type": "string" },
                "endpointId": { "type": "string" },
                "minCu": { "type": "number" },
                "maxCu": { "type": "number" },
                "suspendTimeoutSeconds": { "type": "integer" }
              },
              "required": ["projectId", "branchId", "endpointId", "minCu", "maxCu", "suspendTimeoutSeconds"]
            }
          }
        }
        """;
}
