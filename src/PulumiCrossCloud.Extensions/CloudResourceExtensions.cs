using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Extensions;

public static class CloudResourceExtensions
{
    /// <summary>
    /// Adds multiple secrets to a compute resource
    /// </summary>
    public static T WithSecrets<T>(this T compute, params ISecret[] secrets) where T : ICompute
    {
        foreach (var secret in secrets)
        {
            if (compute is Aws.AwsAppRunnerCompute awsAppRunner)
                awsAppRunner.AddRequiredSecret(secret);
            else if (compute is Aws.AwsFargateCompute awsFargate)
                awsFargate.AddRequiredSecret(secret);
            else if (compute is Gcp.GcpCloudRunCompute gcpCloudRun)
                gcpCloudRun.AddRequiredSecret(secret);
            else if (compute is Azure.AzureContainerAppsCompute azureContainerApps)
                azureContainerApps.AddRequiredSecret(secret);
        }
        
        return compute;
    }
    
    /// <summary>
    /// Gets a human-readable description of the cloud resource
    /// </summary>
    public static string GetDescription(this ICloudResource resource)
    {
        return resource switch
        {
            ISecret => $"{resource.Provider} Secret: {resource.Name}",
            IContainerRegistry => $"{resource.Provider} Container Registry: {resource.Name}",
            ICompute compute => $"{resource.Provider} {compute.ServiceType}: {resource.Name}",
            IRole => $"{resource.Provider} Role: {resource.Name}",
            _ => $"{resource.Provider} Resource: {resource.Name}"
        };
    }
    
    /// <summary>
    /// Gets all policy statements for a resource with specified permissions
    /// </summary>
    public static IEnumerable<IPolicyStatement> GetAllPolicies(this ICompute compute)
    {
        var policies = new List<IPolicyStatement>();
        
        // Add policies for all required secrets
        foreach (var secret in compute.RequiredSecrets)
        {
            policies.AddRange(secret.GetPolicy(SecretPermissions.Read));
        }
        
        // Add policies for container registry
        policies.AddRange(compute.ContainerRegistry.GetPolicy(
            ContainerRegistryPermissions.Pull, 
            ContainerRegistryPermissions.GetAuthToken
        ));
        
        return policies;
    }
    
    /// <summary>
    /// Creates a summary of all permissions needed for a compute resource
    /// </summary>
    public static string GetPermissionSummary(this ICompute compute)
    {
        var summary = $"Permissions for {compute.GetDescription()}:\n";
        summary += $"- Container Registry: Pull, GetAuthToken\n";
        
        if (compute.RequiredSecrets.Any())
        {
            summary += $"- Secrets ({compute.RequiredSecrets.Count()}): Read\n";
            foreach (var secret in compute.RequiredSecrets)
            {
                summary += $"  * {secret.Name}\n";
            }
        }
        
        return summary;
    }
}