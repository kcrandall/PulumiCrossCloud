using System.Collections.Immutable;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;

namespace PulumiCrossCloud.Plugin.Common;

/// <summary>
/// Base for the pulumi-resource-crosscloud-* providers: owns the HTTP client, dispatches resource
/// lifecycle calls by type token and invokes by function token, and serves the package schema.
/// </summary>
public abstract class ResourceProviderBase : Pulumi.Experimental.Provider.Provider
{
    private readonly string _schema;

    protected ResourceProviderBase(
        string packageName,
        string schema,
        HttpMessageHandler? handler,
        Func<string, string?>? environment,
        Func<TimeSpan, CancellationToken, Task>? delay
    )
    {
        _schema = schema;
        // One client for the plugin process lifetime; the engine runs one provider per process.
        Http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        Http.Timeout = TimeSpan.FromSeconds(60);
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"pulumi-resource-{packageName}/{PluginVersion.Current}"
        );
        Environment = environment ?? System.Environment.GetEnvironmentVariable;
        Delay = delay ?? Task.Delay;
    }

    protected HttpClient Http { get; }

    protected Func<string, string?> Environment { get; }

    protected Func<TimeSpan, CancellationToken, Task> Delay { get; }

    protected Dictionary<string, IResourceHandler> Resources { get; } = [];

    protected Dictionary<
        string,
        Func<ImmutableDictionary<string, PropertyValue>, CancellationToken, Task<InvokeResponse>>
    > Functions { get; } = [];

    /// <summary>
    /// Configuration keys compared by the default <see cref="DiffConfig"/>.
    /// </summary>
    protected abstract IReadOnlyList<string> ConfigKeys { get; }

    public override Task<GetSchemaResponse> GetSchema(
        GetSchemaRequest request,
        CancellationToken ct
    ) => Task.FromResult(new GetSchemaResponse { Schema = _schema });

    public override Task<DiffResponse> DiffConfig(DiffRequest request, CancellationToken ct)
    {
        var olds = request.OldInputs.IsEmpty ? request.OldOutputs : request.OldInputs;
        var diffs = ConfigKeys
            .Where(key =>
                !PropertyValues.SameValue(
                    PropertyValues.Get(olds, key),
                    PropertyValues.Get(request.NewInputs, key)
                )
            )
            .ToList();
        var replaces = ConfigReplaces(olds, request.NewInputs).ToList();

        return Task.FromResult(
            new DiffResponse
            {
                Changes = diffs.Count > 0,
                Diffs = diffs,
                Replaces = replaces,
                Stables = [],
                DeleteBeforeReplace = false,
            }
        );
    }

    /// <summary>
    /// Configuration changes that move resources elsewhere (such as another region) and therefore replace them.
    /// </summary>
    protected virtual IEnumerable<string> ConfigReplaces(
        ImmutableDictionary<string, PropertyValue> olds,
        ImmutableDictionary<string, PropertyValue> news
    ) => [];

    public override Task<CheckResponse> Check(CheckRequest request, CancellationToken ct) =>
        Resource(request.Type).Check(request, ct);

    public override Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Resource(request.Type).Diff(request, ct);

    public override Task<CreateResponse> Create(CreateRequest request, CancellationToken ct) =>
        Resource(request.Type).Create(request, ct);

    public override Task<ReadResponse> Read(ReadRequest request, CancellationToken ct) =>
        Resource(request.Type).Read(request, ct);

    public override Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct) =>
        Resource(request.Type).Update(request, ct);

    public override Task Delete(DeleteRequest request, CancellationToken ct) =>
        Resource(request.Type).Delete(request, ct);

    public override Task<InvokeResponse> Invoke(InvokeRequest request, CancellationToken ct) =>
        Functions.TryGetValue(request.Tok, out var function)
            ? function(request.Args, ct)
            : throw new NotSupportedException($"Unknown function '{request.Tok}'.");

    protected static ConfigureResponse Configured() =>
        new()
        {
            AcceptSecrets = true,
            SupportsPreview = true,
            AcceptResources = false,
            AcceptOutputs = false,
        };

    protected static string? ConfigValue(ConfigureRequest request, string key) =>
        NonEmpty(PropertyValues.GetString(request.Args, key));

    protected string? EnvironmentValue(string name) => NonEmpty(Environment(name));

    /// <summary>
    /// A base URI from an override environment variable (used to point at test servers), or the default.
    /// </summary>
    protected Uri BaseUri(string overrideVariable, Uri defaultUri) =>
        EnvironmentValue(overrideVariable) is { } url ? new Uri(url) : defaultUri;

    protected static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private IResourceHandler Resource(string type) =>
        Resources.TryGetValue(type, out var resource)
            ? resource
            : throw new NotSupportedException($"Unknown resource type '{type}'.");
}
