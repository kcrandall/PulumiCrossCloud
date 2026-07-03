using Pulumi;

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
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">The name of the secret.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="ignoreChangesAfterCreate">If true, ignore changes after creation.</param>
    /// <returns>An <see cref="ISecret"/> instance.</returns>
    public virtual ISecret CreateSecret(
        string name,
        string value,
        bool ignoreChangesAfterCreate = false
    ) => throw new NotSupportedException($"Secrets are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a secret resource with an Output&lt;string&gt; value.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">Secret name.</param>
    /// <param name="value">The Output&lt;string&gt; value for the secret.</param>
    /// <param name="ignoreChangesAfterCreate">If true, ignore changes after creation.</param>
    /// <returns>An <see cref="ISecret"/> instance.</returns>
    public virtual ISecret CreateSecret(
        string name,
        Output<string> value,
        bool ignoreChangesAfterCreate = false
    ) =>
        throw new NotSupportedException(
            $"Output<string> secrets are not supported for provider {_provider}."
        );

    /// <summary>
    /// Creates a container registry resource. See implementation for details.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="repositoryId">The name of the registry.</param>
    /// <param name="createDefaultImage">If true, imports a default image (e.g., Hello World) into the registry.</param>
    /// <returns>An <see cref="IContainerRegistry"/> instance.</returns>
    public virtual IContainerRegistry CreateContainerRegistry(
        string name,
        bool createDefaultImage = true
    ) => throw new NotSupportedException($"Container registries are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a container image resource. See implementation for details.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">The name of the container image. Not pulumi resource can be duplicated.</param>
    /// <param name="registry">The container registry to use.</param>
    /// <returns>An <see cref="IContainerImage"/> instance.</returns>
    public virtual IContainerImage CreateContainerImage(string name, IContainerRegistry registry) =>
        throw new NotSupportedException($"Container images are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a compute resource (e.g., Cloud Run, App Runner). See implementation for details.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">The name of the compute resource.</param>
    /// <param name="registry">The container registry to use.</param>
    /// <param name="imageName">The image name to deploy.</param>
    /// <param name="imageTag">The image tag to deploy.</param>
    /// <param name="containerImage">The container image resource (optional, for dependency tracking).</param>
    /// <param name="serviceType">The compute service type (optional).</param>
    /// <returns>An <see cref="ICompute"/> instance.</returns>
    public virtual ICompute CreateCompute(
        string name,
        IContainerRegistry registry,
        string? imageName = null,
        string imageTag = "latest",
        IContainerImage? containerImage = null,
        ComputeServiceType? serviceType = null
    ) => throw new NotSupportedException($"Compute resources are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a role resource. See implementation for details.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">The name of the role.</param>
    /// <param name="assumeRolePolicy">The assume role policy (if applicable).</param>
    /// <returns>An <see cref="IRole"/> instance.</returns>
    public virtual IRole CreateRole(string name, string assumeRolePolicy) =>
        throw new NotSupportedException($"Roles are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a bucket resource if supported by the provider.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">Bucket name.</param>
    /// <param name="isPublic">If true, grant public read access to objects.</param>
    /// <param name="cdn">Optional CDN configuration to front the bucket.</param>
    /// <param name="versioningEnabled">Whether to enable versioning for the bucket.</param>
    /// <param name="corsRules">Optional CORS rules to configure for the bucket.</param>
    /// <param name="indexDocument">Optional default index document (e.g. "index.html").</param>
    /// <returns>An <see cref="IBucket"/> instance.</returns>
    public virtual IBucket CreateBucket(
        string name,
        bool isPublic = false,
        BucketCdnConfiguration? cdn = null,
        bool versioningEnabled = false,
        IEnumerable<CorsRule>? corsRules = null,
        string? indexDocument = null
    ) => throw new NotSupportedException($"Buckets are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a new DNS zone resource.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name for the zone (e.g., "example.com").</param>
    /// <returns>An <see cref="IDnsZone"/> instance.</returns>
    public virtual IDnsZone CreateDnsZone(string name, string domainName) =>
        throw new NotSupportedException($"DNS zones are not supported for provider {_provider}.");

    /// <summary>
    /// Looks up an existing DNS zone by domain name.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name of the zone to look up.</param>
    /// <returns>An <see cref="IDnsZone"/> instance.</returns>
    public virtual IDnsZone LookupDnsZone(string name, string domainName) =>
        throw new NotSupportedException($"DNS zone lookup is not supported for provider {_provider}.");

    /// <summary>
    /// Creates an email service resource.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="domainName">The domain name to set up for email (e.g. "mail.example.com").</param>
    /// <param name="zone">Optional DNS zone to configure records in.</param>
    /// <returns>An <see cref="IEmailService"/> instance.</returns>
    public virtual IEmailService CreateEmailService(string name, string domainName, IDnsZone? zone = null) =>
        throw new NotSupportedException($"Email service is not supported for provider {_provider}.");

    /// <summary>
    /// Creates a Postgres database resource.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    /// <param name="name">Pulumi resource name.</param>
    /// <param name="options">Postgres-specific configuration options.</param>
    /// <returns>An <see cref="IPostgresDatabase"/> instance.</returns>
    public virtual IPostgresDatabase CreatePostgresDatabase(
        string name,
        IPostgresOptions? options = null
    ) => throw new NotSupportedException($"Postgres databases are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a point-to-point work queue.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    public virtual IQueueEndpoint CreateQueue(string name, QueueArgs? args = null) =>
        throw new NotSupportedException($"Queues are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a fan-out pub/sub topic.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    public virtual ITopicEndpoint CreateTopic(string name, TopicArgs? args = null) =>
        throw new NotSupportedException($"Topics are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a replayable event stream.
    /// Default implementation throws NotSupportedException.
    /// </summary>
    public virtual IStreamEndpoint CreateStream(string name, StreamArgs? args = null) =>
        throw new NotSupportedException($"Streams are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a memory cache database (e.g. Redis, Valkey).
    /// Default implementation throws NotSupportedException.
    /// </summary>
    public virtual IMemoryCacheDatabase CreateMemoryCache(string name, MemoryCacheOptions? options = null) =>
        throw new NotSupportedException($"Memory caches are not supported for provider {_provider}.");

    /// <summary>
    /// Creates a search database resource (e.g. OpenSearch, Elasticsearch).
    /// Default implementation throws NotSupportedException.
    /// </summary>
    public virtual ISearchDatabase CreateSearchDatabase(
        string name,
        SearchDatabaseType type,
        ISearchDatabaseOptions? options = null
    ) => throw new NotSupportedException($"Search databases are not supported for provider {_provider}.");
}
