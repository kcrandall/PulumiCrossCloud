namespace PulumiCrossCloud.Core;

/// <summary>
/// Creates compute resources with strongly typed, provider-specific options.
/// </summary>
/// <typeparam name="TComputeProviderOptions">
/// The provider-specific compute options accepted by the factory.
/// </typeparam>
public interface IComputeFactory<in TComputeProviderOptions>
    where TComputeProviderOptions : class, IComputeProviderOptions
{
    /// <summary>
    /// Creates a compute resource with optional provider-specific options.
    /// </summary>
    ICompute CreateCompute(
        string name,
        IContainerRegistry registry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        ComputeServiceType? serviceType = null,
        TComputeProviderOptions? providerOptions = null
    );
}
