namespace PulumiCrossCloud.Core;

/// <summary>
/// Abstract factory for creating cloud resources in a cloud-agnostic way.
/// Implementations should inherit XML documentation from their respective resource implementations.
/// </summary>
public abstract class CloudResourceFactory
{
    protected readonly CloudProvider _provider;

    /// <summary>
    /// Initializes the factory for a specific cloud provider.
    /// </summary>
    /// <param name="provider">The cloud provider.</param>
    protected CloudResourceFactory(CloudProvider provider)
    {
        _provider = provider;
    }

    /// <summary>
    /// Creates a secret resource. See implementation for details.
    /// </summary>
    /// <param name="name">The name of the secret.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="ignoreChangesAfterCreate">If true, ignore changes after creation.</param>
    /// <returns>An <see cref="ISecret"/> instance.</returns>
    public abstract ISecret CreateSecret(
        string name,
        string value,
        bool ignoreChangesAfterCreate = false
    );

    /// <summary>
    /// Creates a container registry resource. See implementation for details.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="repositoryId">The name of the registry.</param>
    /// <param name="createDefaultImage">If true, imports a default image (e.g., Hello World) into the registry.</param>
    /// <returns>An <see cref="IContainerRegistry"/> instance.</returns>
    public abstract IContainerRegistry CreateContainerRegistry(
        string name,
        bool createDefaultImage = true
    );

    /// <summary>
    /// Creates a container image resource. See implementation for details.
    /// </summary>
    /// <param name="name">The name of the container image. Not pulumi resource can be duplicated.</param>
    /// <param name="registry">The container registry to use.</param>
    /// <returns>An <see cref="IContainerImage"/> instance.</returns>
    public abstract IContainerImage CreateContainerImage(string name, IContainerRegistry registry);

    /// <summary>
    /// Creates a compute resource (e.g., Cloud Run, App Runner). See implementation for details.
    /// </summary>
    /// <param name="name">The name of the compute resource.</param>
    /// <param name="registry">The container registry to use.</param>
    /// <param name="imageName">The image name to deploy.</param>
    /// <param name="imageTag">The image tag to deploy.</param>
    /// <param name="containerImage">The container image resource (optional, for dependency tracking).</param>
    /// <param name="serviceType">The compute service type (optional).</param>
    /// <returns>An <see cref="ICompute"/> instance.</returns>
    public abstract ICompute CreateCompute(
        string name,
        IContainerRegistry registry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        ComputeServiceType? serviceType = null
    );

    /// <summary>
    /// Creates a role resource. See implementation for details.
    /// </summary>
    /// <param name="name">The name of the role.</param>
    /// <param name="assumeRolePolicy">The assume role policy (if applicable).</param>
    /// <returns>An <see cref="IRole"/> instance.</returns>
    public abstract IRole CreateRole(string name, string assumeRolePolicy);
}
