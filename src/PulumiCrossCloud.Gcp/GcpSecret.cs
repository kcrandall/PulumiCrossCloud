using Pulumi;
using Pulumi.Gcp.SecretManager.Inputs;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpSecret : ISecret
{
    private readonly Pulumi.Gcp.SecretManager.Secret _secret;
    private readonly Pulumi.Gcp.SecretManager.SecretVersion _secretVersion;

    public GcpSecret(string name, string value, bool ignoreChangesAfterCreate = false)
    {
        Name = name;
        IgnoreChangesAfterCreate = ignoreChangesAfterCreate;
        var projectId = GcpProjectConfig.ProjectId;
        // Ensure Secret Manager API is enabled for this project (via central manager)
        var secretManagerService = GcpProjectServices.EnsureEnabled("secretmanager.googleapis.com");
        var dependsOn = new[] { secretManagerService };
        _secret = new Pulumi.Gcp.SecretManager.Secret(
            name,
            new Pulumi.Gcp.SecretManager.SecretArgs
            {
                SecretId = name,
                Project = projectId,
                Replication = new SecretReplicationArgs { Auto = new SecretReplicationAutoArgs { } }
            },
            new Pulumi.CustomResourceOptions { DependsOn = dependsOn }
        );
        var secretVersionArgs = new Pulumi.Gcp.SecretManager.SecretVersionArgs
        {
            Secret = _secret.Id,
            SecretData = value
        };
        var options = ignoreChangesAfterCreate
            ? new CustomResourceOptions { IgnoreChanges = ["secretData"] }
            : null;
        _secretVersion = new Pulumi.Gcp.SecretManager.SecretVersion(
            $"{name}-version",
            secretVersionArgs,
            options
        );
    }

    public string Name { get; }
    public Output<string> Arn => _secret.Name; // GCP uses resource names instead of ARNs
    public CloudProvider Provider => CloudProvider.GCP;
    public Output<string?> Value => _secretVersion.SecretData; // Would need to get version value
    public bool IgnoreChangesAfterCreate { get; }
    public Pulumi.Resource Resource => _secret;

    public async Task<string> GetValueAsync()
    {
        var projectId = GcpProjectConfig.ProjectId;
        var client = new Google.Cloud.SecretManager.V1.SecretManagerServiceClientBuilder().Build();
        var secretName = Google.Cloud.SecretManager.V1.SecretName.FromProjectSecret(
            projectId,
            Name
        );
        var versions = client.ListSecretVersions(secretName);
        var latestVersion = versions.FirstOrDefault(v =>
            v.State == Google.Cloud.SecretManager.V1.SecretVersion.Types.State.Enabled
        );
        if (latestVersion is null)
            throw new InvalidOperationException($"No enabled secret version found for {Name}");
        var versionName = latestVersion.Name;
        var accessResponse = await client.AccessSecretVersionAsync(versionName);
        return accessResponse.Payload.Data.ToStringUtf8();
    }

    public void SetValue(string value)
    {
        var projectId = GcpProjectConfig.ProjectId;
        var client = new Google.Cloud.SecretManager.V1.SecretManagerServiceClientBuilder().Build();
        var secretName = Google.Cloud.SecretManager.V1.SecretName.FromProjectSecret(
            projectId,
            Name
        );
        client.AddSecretVersion(
            secretName,
            new Google.Cloud.SecretManager.V1.SecretPayload
            {
                Data = Google.Protobuf.ByteString.CopyFromUtf8(value)
            }
        );
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params SecretPermissions[] permissions)
    {
        var statements = new List<IPolicyStatement>();

        foreach (var permission in permissions)
        {
            switch (permission)
            {
                case SecretPermissions.Read:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/secretmanager.secretAccessor" },
                            Resources = new Output<string>[] { Arn } // Fixed to use Output<string>[]
                        }
                    );
                    break;
                case SecretPermissions.Write:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/secretmanager.secretVersionAdder" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
                case SecretPermissions.List:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/secretmanager.viewer" },
                            Resources = new Output<string>[]
                            {
                                Arn.Apply(arn => arn.Replace($"/secrets/{Name}", "/secrets/*"))
                            }
                        }
                    );
                    break;
                case SecretPermissions.Delete:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/secretmanager.admin" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
                case SecretPermissions.GetMetadata:
                    statements.Add(
                        new GcpPolicyStatement
                        {
                            Effect = "Allow",
                            Actions = new[] { "roles/secretmanager.viewer" },
                            Resources = new Output<string>[] { Arn }
                        }
                    );
                    break;
            }
        }

        return statements;
    }
}
