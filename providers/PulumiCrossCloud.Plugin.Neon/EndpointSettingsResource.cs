using System.Collections.Immutable;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Neon;

/// <summary>
/// <c>crosscloud-neon:index:EndpointSettings</c>: autoscaling limits and the suspend timeout of a
/// branch's existing read/write compute. The Terraform-bridged <c>neon</c> provider only sets
/// project defaults for new computes and drops a suspend timeout of -1, so this updates the live
/// compute in place. It never creates or deletes a compute, and a setting left unset is not
/// managed. Deleting the resource leaves the compute's settings as they are.
/// </summary>
internal sealed class EndpointSettingsResource(
    Func<NeonApiClient> client,
    Func<TimeSpan, CancellationToken, Task> delay
) : IResourceHandler
{
    public const string Type = "crosscloud-neon:index:EndpointSettings";

    private const string ProjectId = "projectId";
    private const string BranchId = "branchId";
    private const string MinCu = "minCu";
    private const string MaxCu = "maxCu";
    private const string SuspendTimeoutSeconds = "suspendTimeoutSeconds";

    private const int ConfirmAttempts = 30;
    private static readonly TimeSpan ConfirmInterval = TimeSpan.FromSeconds(2);

    private static readonly string[] Inputs =
    [
        ProjectId,
        BranchId,
        MinCu,
        MaxCu,
        SuspendTimeoutSeconds,
    ];
    private static readonly string[] Settings = [MinCu, MaxCu, SuspendTimeoutSeconds];
    private static readonly IReadOnlySet<string> ReplaceInputs = new HashSet<string>
    {
        ProjectId,
        BranchId,
    };
    private static readonly IReadOnlySet<string> RequiredInputs = new HashSet<string>
    {
        ProjectId,
        BranchId,
    };

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        foreach (var key in RequiredInputs)
            if (
                !PropertyValues.IsComputed(inputs, key)
                && string.IsNullOrWhiteSpace(PropertyValues.GetString(inputs, key))
            )
                failures.Add(new CheckFailure(key, $"{key} is required."));

        var minCu = PropertyValues.GetNumber(inputs, MinCu);
        var maxCu = PropertyValues.GetNumber(inputs, MaxCu);
        if (minCu is < 0.25)
            failures.Add(new CheckFailure(MinCu, "minCu must be at least 0.25."));
        if (maxCu is < 0.25)
            failures.Add(new CheckFailure(MaxCu, "maxCu must be at least 0.25."));
        if (minCu > maxCu)
            failures.Add(new CheckFailure(MinCu, "minCu must not exceed maxCu."));
        if (PropertyValues.GetNumber(inputs, SuspendTimeoutSeconds) is { } suspend)
        {
            if (suspend != Math.Floor(suspend))
                failures.Add(
                    new CheckFailure(SuspendTimeoutSeconds, "suspendTimeoutSeconds must be a whole number.")
                );
            else if (suspend is < -1 or > 604800)
                failures.Add(
                    new CheckFailure(
                        SuspendTimeoutSeconds,
                        "suspendTimeoutSeconds must be -1 (never), 0 (Neon's default) or up to 604800."
                    )
                );
        }

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    public Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Task.FromResult(InputDiff.Compute(request, Inputs, ReplaceInputs, RequiredInputs));

    public async Task<CreateResponse> Create(CreateRequest request, CancellationToken ct)
    {
        var inputs = request.Inputs;
        if (request.Preview)
            return new CreateResponse { Outputs = PreviewOutputs(inputs) };

        var projectId = PropertyValues.GetString(inputs, ProjectId)!;
        var branchId = PropertyValues.GetString(inputs, BranchId)!;
        var endpoints = await client().ListEndpointsAsync(projectId, ct);
        var matches = endpoints
            .Where(endpoint => endpoint.BranchId == branchId && endpoint.Type == "read_write")
            .ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"Expected exactly one read/write compute on Neon branch {branchId} of project {projectId}, found {matches.Count}."
            );

        var endpoint = await ApplyAsync(matches[0], inputs, ct);
        return new CreateResponse { Id = endpoint.Id, Outputs = ToOutputs(endpoint, inputs) };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var projectId =
            PropertyValues.GetString(request.Inputs, ProjectId)
            ?? PropertyValues.GetString(request.Outputs, ProjectId);
        if (projectId is null)
            throw new InvalidOperationException(
                "Import Neon endpoint settings with the ID <projectId>/<endpointId>."
            );

        var endpointId = request.Id.Contains('/') ? request.Id.Split('/')[1] : request.Id;
        var endpoint = await client().GetEndpointAsync(projectId, endpointId, ct);
        if (endpoint is null)
            return new ReadResponse();

        // Report drift only for the settings this resource manages.
        var inputs = request.Inputs.IsEmpty
            ? ImmutableDictionary<string, PropertyValue>
                .Empty.Add(ProjectId, new PropertyValue(endpoint.ProjectId))
                .Add(BranchId, new PropertyValue(endpoint.BranchId))
            : request.Inputs;
        foreach (var setting in Settings)
            if (!PropertyValues.Get(inputs, setting).IsNull)
                inputs = inputs.SetItem(setting, LiveValue(endpoint, setting, inputs));

        return new ReadResponse
        {
            Id = endpoint.Id,
            Inputs = inputs,
            Outputs = ToOutputs(endpoint, inputs),
        };
    }

    public async Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        if (request.Preview)
            return new UpdateResponse { Outputs = PreviewOutputs(inputs) };

        var projectId = PropertyValues.GetString(inputs, ProjectId)!;
        var endpoint =
            await client().GetEndpointAsync(projectId, request.Id, ct)
            ?? throw new InvalidOperationException(
                $"Neon compute {request.Id} no longer exists. Run `pulumi refresh` to recreate it."
            );
        endpoint = await ApplyAsync(endpoint, inputs, ct);
        return new UpdateResponse { Outputs = ToOutputs(endpoint, inputs) };
    }

    // The compute belongs to the branch; removing this resource stops managing its settings.
    public Task Delete(DeleteRequest request, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Sends only the settings that differ, then waits until Neon reports them.
    /// </summary>
    private async Task<NeonEndpoint> ApplyAsync(
        NeonEndpoint endpoint,
        ImmutableDictionary<string, PropertyValue> inputs,
        CancellationToken ct
    )
    {
        var desired = Desired(inputs);
        var minCu = desired.MinCu ?? endpoint.AutoscalingLimitMinCu;
        var maxCu = desired.MaxCu ?? endpoint.AutoscalingLimitMaxCu;
        if (minCu > maxCu)
            throw new InvalidOperationException(
                $"Neon compute {endpoint.Id} would have minCu {minCu} above maxCu {maxCu}; set both."
            );

        if (Matches(endpoint, desired))
            return endpoint;

        var api = client();
        endpoint = await api.UpdateEndpointAsync(endpoint.ProjectId, endpoint.Id, desired, ct);
        for (var attempt = 0; !Matches(endpoint, desired); attempt++)
        {
            if (attempt >= ConfirmAttempts)
                throw new TimeoutException(
                    $"Neon did not confirm the compute settings for {endpoint.Id}."
                );
            await delay(ConfirmInterval, ct);
            endpoint =
                await api.GetEndpointAsync(endpoint.ProjectId, endpoint.Id, ct)
                ?? throw new InvalidOperationException(
                    $"Neon compute {endpoint.Id} disappeared while it was being updated."
                );
        }

        return endpoint;
    }

    private static NeonComputeSettings Desired(ImmutableDictionary<string, PropertyValue> inputs)
    {
        var suspend = PropertyValues.GetNumber(inputs, SuspendTimeoutSeconds) is { } value
            ? (int)value
            : (int?)null;
        // Neon reports its default (0) as five minutes.
        if (suspend == 0)
            suspend = 300;
        return new NeonComputeSettings(
            PropertyValues.GetNumber(inputs, MinCu),
            PropertyValues.GetNumber(inputs, MaxCu),
            suspend
        );
    }

    private static bool Matches(NeonEndpoint endpoint, NeonComputeSettings desired) =>
        (desired.MinCu is null || desired.MinCu == endpoint.AutoscalingLimitMinCu)
        && (desired.MaxCu is null || desired.MaxCu == endpoint.AutoscalingLimitMaxCu)
        && (
            desired.SuspendTimeoutSeconds is null
            || desired.SuspendTimeoutSeconds == endpoint.SuspendTimeoutSeconds
        );

    private static PropertyValue LiveValue(
        NeonEndpoint endpoint,
        string setting,
        ImmutableDictionary<string, PropertyValue> inputs
    ) =>
        setting switch
        {
            MinCu => new PropertyValue(endpoint.AutoscalingLimitMinCu),
            MaxCu => new PropertyValue(endpoint.AutoscalingLimitMaxCu),
            // Keep 0 when the input asked for Neon's default and the compute still uses it.
            _ => PropertyValues.GetNumber(inputs, SuspendTimeoutSeconds) == 0
            && endpoint.SuspendTimeoutSeconds == 300
                ? new PropertyValue(0)
                : new PropertyValue(endpoint.SuspendTimeoutSeconds),
        };

    private static ImmutableDictionary<string, PropertyValue> ToOutputs(
        NeonEndpoint endpoint,
        ImmutableDictionary<string, PropertyValue> inputs
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(ProjectId, new PropertyValue(endpoint.ProjectId))
            .Add(BranchId, new PropertyValue(endpoint.BranchId))
            .Add(MinCu, LiveValue(endpoint, MinCu, inputs))
            .Add(MaxCu, LiveValue(endpoint, MaxCu, inputs))
            .Add(SuspendTimeoutSeconds, LiveValue(endpoint, SuspendTimeoutSeconds, inputs))
            .Add("endpointId", new PropertyValue(endpoint.Id));

    private static ImmutableDictionary<string, PropertyValue> PreviewOutputs(
        ImmutableDictionary<string, PropertyValue> inputs
    )
    {
        var outputs = ImmutableDictionary<string, PropertyValue>
            .Empty.Add(ProjectId, PropertyValues.Get(inputs, ProjectId))
            .Add(BranchId, PropertyValues.Get(inputs, BranchId))
            .Add("endpointId", PropertyValue.Computed);
        foreach (var setting in Settings)
        {
            var value = PropertyValues.Get(inputs, setting);
            outputs = outputs.Add(setting, value.IsNull ? PropertyValue.Computed : value);
        }
        return outputs;
    }
}
