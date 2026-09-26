using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

/// <summary>
/// GCP Service Account - different from traditional roles but serves similar purpose
/// </summary>
public class GcpServiceAccount : IRole
{
    private readonly Pulumi.Gcp.ServiceAccount.Account _serviceAccount;

    public GcpServiceAccount(
        string name,
        string displayName = "",
        Pulumi.Gcp.Provider? provider = null
    )
    {
        Name = name;
        var projectId = GcpProjectConfig.ProjectId;
        // Ensure IAM API is enabled before creating service accounts
        var iamApi = GcpProjectServices.EnsureEnabled("iam.googleapis.com", provider);

        _serviceAccount = new Pulumi.Gcp.ServiceAccount.Account(
            name,
            new Pulumi.Gcp.ServiceAccount.AccountArgs
            {
                AccountId = name,
                DisplayName = !string.IsNullOrEmpty(displayName) ? displayName : name,
                Project = projectId,
            },
            new CustomResourceOptions { DependsOn = new[] { iamApi }, Provider = provider }
        );
    }

    public string Name { get; }
    public Output<string> Arn => _serviceAccount.Email; // GCP uses email instead of ARN
    public CloudProvider Provider => CloudProvider.GCP;

    public void AttachPolicies(IEnumerable<IPolicyStatement> policies)
    {
        // In GCP, you'd create IAM bindings for this service account
        throw new NotImplementedException("Use IAM bindings in GCP instead of attached policies");
    }

    public void AttachInlinePolicy(string policyName, IEnumerable<IPolicyStatement> statements)
    {
        // Convert policy statements to GCP IAM bindings
        var gcpStatements = statements.Cast<GcpPolicyStatement>().ToList();

        foreach (var statement in gcpStatements)
        {
            foreach (var action in statement.Actions)
            {
                // Use Output.All to handle multiple resources properly
                var resourcesOutput = Output.All(statement.Resources.ToArray());
                resourcesOutput.Apply(resources =>
                {
                    foreach (var resource in resources)
                        CreateIamBinding(action, resource);

                    return "";
                });
            }
        }
    }

    private void CreateIamBinding(string role, string resource)
    {
        // This is complex because GCP IAM bindings are resource-specific
        // You'd need different Pulumi resources for different resource types
        // For now, just a placeholder

        // Example of what this might look like:
        // new Pulumi.Gcp.Projects.IAMBinding($"{Name}-{role}", new Pulumi.Gcp.Projects.IAMBindingArgs
        // {
        //     Project = GcpProjectConfig.ProjectId,
        //     Role = role,
        //     Members = new[] { $"serviceAccount:{_serviceAccount.Email}" }
        // });

        throw new NotImplementedException(
            $"GCP IAM binding creation for role '{role}' on resource '{resource}' not yet implemented"
        );
    }

    /// <summary>
    /// Gets the service account email for use in IAM bindings
    /// </summary>
    public Output<string> ServiceAccountEmail => _serviceAccount.Email;
}
