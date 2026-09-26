using System.Collections.Immutable;
using System.Text;
using Pulumi.Automation;

namespace PulumiCrossCloud.Integration.Tests.Infrastructure;

/// <summary>
/// An inline Pulumi program deployed through the Automation API into a throwaway local
/// file backend, so the tests never touch Pulumi Cloud or a developer's own stacks.
/// </summary>
public sealed class PulumiStack : IAsyncDisposable
{
    private readonly WorkspaceStack _stack;
    private readonly DirectoryInfo _workDir;
    private bool _destroyed;

    private PulumiStack(WorkspaceStack stack, DirectoryInfo workDir)
    {
        _stack = stack;
        _workDir = workDir;
    }

    public IImmutableDictionary<string, OutputValue> Outputs { get; private set; } =
        ImmutableDictionary<string, OutputValue>.Empty;

    /// <summary>
    /// Creates the stack. <paramref name="config"/> keys are set with path semantics, so
    /// structured provider config such as <c>aws:endpoints[0].s3</c> works.
    /// </summary>
    public static async Task<PulumiStack> CreateAsync(
        string projectName,
        PulumiFn program,
        IReadOnlyDictionary<string, string> config,
        IReadOnlyDictionary<string, string?>? environment = null
    )
    {
        var workDir = Directory.CreateTempSubdirectory("pulumicrosscloud-it-");
        var stateDir = Directory.CreateDirectory(Path.Combine(workDir.FullName, "state"));

        var environmentVariables = new Dictionary<string, string?>
        {
            ["PULUMI_CONFIG_PASSPHRASE"] = "integration-tests",
            ["PULUMI_SKIP_UPDATE_CHECK"] = "true",
            ["PULUMI_SKIP_CONFIRMATIONS"] = "true",
        };
        foreach (var (key, value) in environment ?? new Dictionary<string, string?>())
            environmentVariables[key] = value;

        var stack = await LocalWorkspace.CreateOrSelectStackAsync(
            new InlineProgramArgs(projectName, "floci", program)
            {
                WorkDir = workDir.FullName,
                ProjectSettings = new ProjectSettings(projectName, ProjectRuntimeName.Dotnet)
                {
                    Backend = new ProjectBackend { Url = new Uri(stateDir.FullName).AbsoluteUri },
                },
                EnvironmentVariables = environmentVariables,
            }
        );

        foreach (var (key, value) in config)
            await stack.SetConfigAsync(key, new ConfigValue(value), path: true);

        return new PulumiStack(stack, workDir);
    }

    public async Task UpAsync()
    {
        var result = await _stack.UpAsync(
            new UpOptions
            {
                OnStandardOutput = Console.WriteLine,
                OnStandardError = Console.Error.WriteLine,
            }
        );
        Outputs = result.Outputs;
    }

    /// <summary>
    /// Previews the deployed program again and returns every step that would change a resource.
    /// A correct resource and a faithful emulator produce none.
    /// </summary>
    public async Task<PreviewSummary> PreviewAsync()
    {
        var diff = new StringBuilder();
        var steps = new List<PreviewStep>();
        await _stack.PreviewAsync(
            new PreviewOptions
            {
                Diff = true,
                OnStandardOutput = line => diff.AppendLine(line),
                OnEvent = engineEvent =>
                {
                    if (
                        engineEvent.ResourcePreEvent?.Metadata is { } step
                        && step.Op is not OperationType.Same
                    )
                        lock (steps)
                            steps.Add(
                                new PreviewStep(step.Urn, step.Op, step.Diffs?.ToList() ?? [])
                            );
                },
            }
        );
        return new PreviewSummary(steps, diff.ToString());
    }

    public string Output(string name) =>
        Outputs.TryGetValue(name, out var output)
            ? output.Value?.ToString()
                ?? throw new InvalidOperationException($"Output '{name}' is null.")
            : throw new KeyNotFoundException(
                $"Stack output '{name}' is missing. Outputs: {string.Join(", ", Outputs.Keys)}"
            );

    public async Task DestroyAsync()
    {
        await _stack.DestroyAsync(
            new DestroyOptions
            {
                OnStandardOutput = Console.WriteLine,
                OnStandardError = Console.Error.WriteLine,
            }
        );
        _destroyed = true;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_destroyed)
                await DestroyAsync();
            await _stack.Workspace.RemoveStackAsync(_stack.Name);
        }
        finally
        {
            _stack.Dispose();
            _workDir.Delete(recursive: true);
        }
    }
}

public sealed record PreviewStep(
    string Urn,
    OperationType Operation,
    IReadOnlyList<string> ChangedProperties
);

public sealed record PreviewSummary(IReadOnlyList<PreviewStep> Steps, string Diff)
{
    /// <summary>
    /// Steps other than in-place updates confined to <paramref name="knownEmulatorGaps"/>,
    /// properties the emulator is known not to round-trip.
    /// </summary>
    public IReadOnlyList<PreviewStep> UnexpectedSteps(params string[] knownEmulatorGaps) =>
        Steps
            .Where(step =>
                step.Operation is not OperationType.Update
                || step.ChangedProperties.Count == 0
                || !step.ChangedProperties.All(knownEmulatorGaps.Contains)
            )
            .ToList();
}
