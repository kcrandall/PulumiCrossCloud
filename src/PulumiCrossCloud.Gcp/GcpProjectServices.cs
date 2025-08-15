using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Pulumi;
using Pulumi.Gcp.Projects;

namespace PulumiCrossCloud.Gcp;

/// <summary>
/// Ensures required Google Cloud APIs are enabled per project and exposes
/// Pulumi dependency resources that other constructs can depend on.
/// </summary>
public static class GcpProjectServices
{
    private static readonly ConcurrentDictionary<string, Service> _enabledServicesByProjectAndName =
        new();

    /// <summary>
    /// Ensure the given API service is enabled in the current project and return the Pulumi Service resource.
    /// Automatically ensures Cloud Resource Manager API is enabled first for all other services.
    /// </summary>
    public static Service EnsureEnabled(string serviceName)
    {
        var projectId = GcpProjectConfig.ProjectId;
        var key = $"{projectId}:{serviceName}";

        return _enabledServicesByProjectAndName.GetOrAdd(
            key,
            _ =>
            {
                var dependsOn = new List<Resource>();
                if (
                    !string.Equals(
                        serviceName,
                        "cloudresourcemanager.googleapis.com",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    var crm = EnsureEnabled("cloudresourcemanager.googleapis.com");
                    dependsOn.Add(crm);
                }

                var resourceName = $"{serviceName.Replace('.', '-')}-api-{projectId}";
                return new Service(
                    resourceName,
                    new ServiceArgs
                    {
                        Project = projectId,
                        ServiceName = serviceName,
                        DisableDependentServices = false
                    },
                    new CustomResourceOptions { DependsOn = dependsOn.ToArray() }
                );
            }
        );
    }

    /// <summary>
    /// Convenience method to get dependency resources for multiple services.
    /// </summary>
    public static Resource[] DependsOn(params string[] serviceNames)
    {
        return serviceNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(EnsureEnabled)
            .Cast<Resource>()
            .ToArray();
    }
}
