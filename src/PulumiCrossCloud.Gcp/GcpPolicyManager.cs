using System.Collections.Generic;
using System.Linq;
using Pulumi;
using Pulumi.Gcp.PubSub;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

/// <summary>
/// Handles the application of GCP IAM policies by creating appropriate IAM binding resources.
/// </summary>
public static class GcpPolicyManager
{
    /// <summary>
    /// Applies a policy statement by creating necessary IAM Member resources.
    /// </summary>
    /// <param name="namePrefix">Prefix for the generated Pulumi resource names.</param>
    /// <param name="policy">The policy statement containing roles and resources.</param>
    /// <param name="serviceAccountEmail">The service account email to grant permissions to.</param>
    /// <param name="projectId">The GCP project ID.</param>
    /// <param name="provider">The Pulumi GCP provider.</param>
    /// <param name="dependencies">List to add generated resources to for dependency tracking.</param>
    public static void ApplyPolicy(
        string namePrefix,
        GcpPolicyStatement policy,
        Output<string> serviceAccountEmail,
        string projectId,
        Pulumi.Gcp.Provider? provider,
        List<Resource> dependencies
    )
    {
        var member = Output.Format($"serviceAccount:{serviceAccountEmail}");
        var resourceList = policy.Resources.ToList();
        var nameList = policy.ResourceNames.ToList();

        foreach (var role in policy.Actions)
        {
            for (int i = 0; i < resourceList.Count; i++)
            {
                var resourceId = resourceList[i];

                // Use provided stable name if available, otherwise fall back to index (less ideal but necessary for non-named resources)
                string stableNamePart = i < nameList.Count ? nameList[i] : i.ToString();

                // Sanitize role and name for use in Pulumi resource name
                var safeRole = role.Replace("/", "-").Replace(".", "-");
                var resourceName = $"{namePrefix}-{safeRole}-{stableNamePart}";

                if (role == "roles/pubsub.publisher")
                {
                    // Publisher role applies to Topics
                    var binding = new TopicIAMMember(
                        resourceName,
                        new TopicIAMMemberArgs
                        {
                            Project = projectId,
                            Topic = resourceId,
                            Role = role,
                            Member = member,
                        },
                        new CustomResourceOptions { Provider = provider }
                    );
                    dependencies.Add(binding);
                }
                else if (role == "roles/pubsub.subscriber")
                {
                    // Subscriber role applies to Subscriptions
                    var binding = new SubscriptionIAMMember(
                        resourceName,
                        new SubscriptionIAMMemberArgs
                        {
                            Project = projectId,
                            Subscription = resourceId,
                            Role = role,
                            Member = member,
                        },
                        new CustomResourceOptions { Provider = provider }
                    );
                    dependencies.Add(binding);
                }
                // Add more role handlers here as needed
            }
        }
    }
}
