namespace PulumiCrossCloud.Bird.Provider;

/// <summary>
/// Pulumi package schema, served for <c>pulumi package get-schema</c> and SDK generation.
/// The C# SDK in PulumiCrossCloud.Bird is hand-written against these type tokens.
/// </summary>
internal static class BirdSchema
{
    public const string Json = """
        {
          "name": "bird",
          "displayName": "Bird",
          "description": "Manage Bird (bird.com) email sending domains.",
          "publisher": "PulumiCrossCloud",
          "repository": "https://github.com/kcrandall/PulumiCrossCloud",
          "pluginDownloadURL": "github://api.github.com/kcrandall/PulumiCrossCloud",
          "config": {
            "variables": {
              "apiKey": { "type": "string", "secret": true, "description": "Bird API key. Defaults to BIRD_API_KEY." },
              "region": { "type": "string", "description": "Bird region: us1 or eu1. Defaults to BIRD_REGION, then the API key's region." }
            }
          },
          "provider": {
            "inputProperties": {
              "apiKey": { "type": "string", "secret": true },
              "region": { "type": "string" }
            }
          },
          "types": {
            "bird:index:DnsRecord": {
              "type": "object",
              "properties": {
                "type": { "type": "string" },
                "name": { "type": "string", "description": "Record name relative to the registered domain's zone." },
                "host": { "type": "string", "description": "Fully qualified record name." },
                "value": { "type": "string" },
                "purpose": { "type": "string", "description": "dkim, return_path, dmarc, tracking, or inbound_mx." },
                "state": { "type": "string", "description": "active, pending (staged change), or deprecated." },
                "optional": { "type": "boolean" },
                "status": { "type": "string" }
              },
              "required": ["type", "name", "host", "value", "purpose", "state", "optional", "status"]
            }
          },
          "resources": {
            "bird:index:SendingDomain": {
              "description": "A Bird email sending domain.",
              "inputProperties": {
                "domain": { "type": "string", "willReplaceOnChanges": true },
                "returnPathName": { "type": "string", "description": "Return-path name part. Bird defaults to send." },
                "trackingName": { "type": "string", "description": "Tracking name part. Bird defaults to links; an empty string disables tracking." },
                "clickTracking": { "type": "boolean" },
                "openTracking": { "type": "boolean" }
              },
              "requiredInputs": ["domain"],
              "properties": {
                "domain": { "type": "string" },
                "region": { "type": "string" },
                "workspaceId": { "type": "string" },
                "status": { "type": "string" },
                "sendingStatus": { "type": "string" },
                "returnPathName": { "type": "string" },
                "returnPathDomain": { "type": "string" },
                "trackingName": { "type": "string" },
                "trackingDomain": { "type": "string" },
                "clickTracking": { "type": "boolean" },
                "openTracking": { "type": "boolean" },
                "dkimSelector": { "type": "string" },
                "verifiedAt": { "type": "string" },
                "dnsRecords": { "type": "array", "items": { "$ref": "#/types/bird:index:DnsRecord" } }
              },
              "required": ["domain", "region", "status", "returnPathName", "trackingName", "clickTracking", "openTracking", "dnsRecords"]
            },
            "bird:index:DomainVerification": {
              "description": "Runs Bird's DNS check for a sending domain and waits until it may send.",
              "inputProperties": {
                "domainId": { "type": "string", "willReplaceOnChanges": true },
                "waitForSending": { "type": "boolean", "description": "Wait until sending is verified. Defaults to true." },
                "triggers": { "type": "array", "items": { "type": "string" }, "willReplaceOnChanges": true }
              },
              "requiredInputs": ["domainId"],
              "properties": {
                "domainId": { "type": "string" },
                "waitForSending": { "type": "boolean" },
                "triggers": { "type": "array", "items": { "type": "string" } },
                "status": { "type": "string" },
                "sendingStatus": { "type": "string" },
                "verifiedAt": { "type": "string" }
              },
              "required": ["domainId", "waitForSending", "status"]
            }
          }
        }
        """;
}
