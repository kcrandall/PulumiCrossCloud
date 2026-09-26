using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Pulumi;
using Pulumi.Gcp.Projects;

namespace PulumiCrossCloud.Gcp;

/// <summary>
/// Ensures required Google Cloud APIs are enabled per project and exposes
/// Pulumi dependency resources that other constructs can depend on.
/// </summary>
public static class GcpProjectServices
{
    // Keyed by deployment: an Automation API host runs several deployments in one process, and a
    // resource registered by an earlier deployment does not exist in a later one.
    private static readonly ConditionalWeakTable<
        DeploymentInstance,
        ConcurrentDictionary<string, Service>
    > _enabledServicesByDeployment = new();

    /// <summary>
    /// Ensure the given API service is enabled in the current project and return the Pulumi Service resource.
    /// Automatically ensures Cloud Resource Manager API is enabled first for all other services.
    /// </summary>
    /// <param name="serviceName">The API to enable, e.g. <c>secretmanager.googleapis.com</c>.</param>
    /// <param name="provider">
    /// The provider to enable the API through; null uses the default provider. Each provider gets
    /// its own resource, named after the provider so two providers cannot collide.
    /// </param>
    public static Service EnsureEnabled(string serviceName, Pulumi.Gcp.Provider? provider = null)
    {
        var projectId = GcpProjectConfig.ProjectId;
        var providerSuffix = provider is null ? "" : $"-{provider.GetResourceName()}";
        var key = $"{projectId}:{serviceName}{providerSuffix}";

        var enabledServicesByProjectAndName = _enabledServicesByDeployment.GetValue(
            Deployment.Instance,
            _ => new ConcurrentDictionary<string, Service>()
        );

        return enabledServicesByProjectAndName.GetOrAdd(
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
                    var crm = EnsureEnabled("cloudresourcemanager.googleapis.com", provider);
                    dependsOn.Add(crm);
                }

                var resourceName =
                    $"{serviceName.Replace('.', '-')}-api-{projectId}{providerSuffix}";
                return new Service(
                    resourceName,
                    new ServiceArgs
                    {
                        Project = projectId,
                        ServiceName = serviceName,
                        DisableDependentServices = false,
                        DisableOnDestroy = false, // Keep APIs enabled when stack is destroyed
                    },
                    new CustomResourceOptions
                    {
                        DependsOn = dependsOn.ToArray(),
                        Provider = provider,
                    }
                );
            }
        );
    }

    /// <summary>
    /// Convenience method to get dependency resources for multiple services.
    /// </summary>
    public static Resource[] DependsOn(params string[] serviceNames) =>
        DependsOn(provider: null, serviceNames);

    /// <summary>
    /// Convenience method to get dependency resources for multiple services enabled through
    /// <paramref name="provider"/>.
    /// </summary>
    public static Resource[] DependsOn(Pulumi.Gcp.Provider? provider, params string[] serviceNames)
    {
        return serviceNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(serviceName => EnsureEnabled(serviceName, provider))
            .Cast<Resource>()
            .ToArray();
    }
}
