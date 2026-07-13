using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aiven;

public class AivenCloudResourceFactory : CloudResourceFactory
{
    private Input<string>? _projectName;
    private string? _defaultCloud;
    private global::Pulumi.Aiven.Project? _managedProject;
    private readonly global::Pulumi.Aiven.Provider? _aivenProvider;

    /// <summary>
    /// Initializes a factory with an optional API token and default cloud.
    /// </summary>
    public AivenCloudResourceFactory(string? apiToken = null, string? defaultCloud = null)
        : base(CloudProvider.Aiven)
    {
        _defaultCloud = defaultCloud;
        if (!string.IsNullOrEmpty(apiToken))
            _aivenProvider = new global::Pulumi.Aiven.Provider(
                "aiven-provider",
                new global::Pulumi.Aiven.ProviderArgs { ApiToken = apiToken }
            );
    }

    /// <summary>
    /// Initializes a factory and immediately creates a managed project.
    /// </summary>
    public AivenCloudResourceFactory(
        string projectName,
        global::Pulumi.Aiven.ProjectArgs projectArgs,
        string? apiToken = null,
        string? defaultCloud = null
    )
        : this(apiToken, defaultCloud)
    {
        CreateProject(projectName, projectArgs, setDefault: true);
    }

    /// <summary>
    /// Initializes a factory with an existing project name and optional API token.
    /// </summary>
    public AivenCloudResourceFactory(
        string projectName,
        string? apiToken = null,
        string? defaultCloud = null
    )
        : this(apiToken, defaultCloud)
    {
        _projectName = (Input<string>)projectName;
    }

    /// <summary>
    /// Initializes a factory with a custom Aiven provider and optional project name.
    /// </summary>
    public AivenCloudResourceFactory(
        global::Pulumi.Aiven.Provider provider,
        string? projectName = null,
        string? defaultCloud = null
    )
        : base(CloudProvider.Aiven)
    {
        _aivenProvider = provider;
        _defaultCloud = defaultCloud;
        if (projectName != null)
            _projectName = (Input<string>)projectName;
    }

    /// <summary>
    /// Creates a new Aiven project and optionally sets it as the default project for this factory.
    /// </summary>
    public global::Pulumi.Aiven.Project CreateProject(
        string name,
        global::Pulumi.Aiven.ProjectArgs? args = null,
        bool setDefault = false
    )
    {
        var project = new global::Pulumi.Aiven.Project(
            name,
            args ?? new global::Pulumi.Aiven.ProjectArgs { ProjectName = name },
            new CustomResourceOptions { Provider = _aivenProvider }
        );

        if (setDefault)
        {
            _managedProject = project;
            _projectName = project.ProjectName;
        }

        return project;
    }

    /// <summary>
    /// Creates a factory and immediately initializes a managed project.
    /// </summary>
    public static AivenCloudResourceFactory CreateWithProject(
        string projectName,
        global::Pulumi.Aiven.ProjectArgs? args = null,
        string? apiToken = null,
        string? defaultCloud = null
    )
    {
        var factory = new AivenCloudResourceFactory(apiToken, defaultCloud);
        factory.CreateProject(projectName, args, setDefault: true);
        return factory;
    }

    public override ISearchDatabase CreateSearchDatabase(
        string name,
        SearchDatabaseType type,
        ISearchDatabaseOptions? options = null
    )
    {
        var aivenOptions = options as IAivenDatabaseOptions;
        var project =
            aivenOptions?.Project != null
                ? (Input<string>)aivenOptions.Project
                : _projectName
                    ?? throw new InvalidOperationException(
                        "Project name must be provided either in options or constructor."
                    );
        var searchOptions = options as AivenSearchDatabaseOptions;
        var plan = ResolveSearchPlan(searchOptions, aivenOptions);
        var cloud = ResolveSearchCloud(aivenOptions, plan);

        return new AivenSearchDatabase(name, project, type, plan, cloud, _aivenProvider);
    }

    public override IPostgresDatabase CreatePostgresDatabase(
        string name,
        IPostgresOptions? options = null
    )
    {
        var aivenOptions = options as IAivenDatabaseOptions;
        var project =
            aivenOptions?.Project != null
                ? (Input<string>)aivenOptions.Project
                : _projectName
                    ?? throw new InvalidOperationException(
                        "Project name must be provided either in options or constructor."
                    );

        var plan = aivenOptions?.Plan ?? "startup-4";

        return new AivenPostgresDatabase(
            name,
            project,
            plan,
            options?.Region ?? aivenOptions?.Cloud ?? _defaultCloud,
            _aivenProvider
        );
    }

    private string ResolveSearchPlan(
        AivenSearchDatabaseOptions? searchOptions,
        IAivenDatabaseOptions? aivenOptions
    )
    {
        if (searchOptions?.UseFreeTier == true)
            return AivenSearchDatabaseOptions.FreeOpenSearchPlan;

        return aivenOptions?.Plan ?? "startup-4";
    }

    private string? ResolveSearchCloud(IAivenDatabaseOptions? aivenOptions, string plan)
    {
        if (!string.IsNullOrWhiteSpace(aivenOptions?.Cloud))
            return aivenOptions.Cloud;

        if (
            string.Equals(
                plan,
                AivenSearchDatabaseOptions.FreeOpenSearchPlan,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return AivenSearchDatabaseOptions.DefaultFreeOpenSearchCloud;

        return _defaultCloud;
    }
}
