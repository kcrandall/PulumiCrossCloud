using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Koyeb;

/// <summary>
/// <c>crosscloud-koyeb:index:ServiceSleep</c>: the scale-to-zero light and deep sleep delays of a
/// Koyeb service, which the Terraform-bridged <c>koyeb</c> provider cannot set. It adds a
/// <c>sleep_idle_delay</c> target to each scaling of the service's latest deployment definition
/// and redeploys without rebuilding, then waits for the deployment. A change to <c>triggers</c>
/// (such as the service's version) applies the delays again, because a redeploy by the bridged
/// provider replaces the definition. Deleting it leaves the service unchanged.
/// </summary>
internal sealed class ServiceSleepResource(
    Func<KoyebApiClient> client,
    Func<TimeSpan, CancellationToken, Task> delay
) : IResourceHandler
{
    public const string Type = "crosscloud-koyeb:index:ServiceSleep";

    private const string ServiceId = "serviceId";
    private const string LightSleep = "lightSleepDelaySeconds";
    private const string DeepSleep = "deepSleepDelaySeconds";
    private const string Triggers = "triggers";

    private static readonly string[] Inputs = [ServiceId, LightSleep, DeepSleep, Triggers];
    private static readonly IReadOnlySet<string> ReplaceInputs = new HashSet<string> { ServiceId };
    private static readonly IReadOnlySet<string> RequiredInputs = new HashSet<string>
    {
        ServiceId,
    };

    private static readonly HashSet<string> Running = ["HEALTHY", "SLEEPING"];
    private static readonly HashSet<string> Failed =
    [
        "CANCELING",
        "CANCELED",
        "DEGRADED",
        "UNHEALTHY",
        "STOPPING",
        "STOPPED",
        "ERRORING",
        "ERROR",
    ];

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        if (
            !PropertyValues.IsComputed(inputs, ServiceId)
            && string.IsNullOrWhiteSpace(PropertyValues.GetString(inputs, ServiceId))
        )
            failures.Add(new CheckFailure(ServiceId, "serviceId is required."));

        var light = PropertyValues.GetNumber(inputs, LightSleep);
        var deep = PropertyValues.GetNumber(inputs, DeepSleep);
        foreach (var (key, value) in new[] { (LightSleep, light), (DeepSleep, deep) })
            if (value is { } seconds && (seconds < 0 || seconds != Math.Floor(seconds)))
                failures.Add(new CheckFailure(key, $"{key} must be a whole number of seconds."));
        if (
            light is null
            && deep is null
            && !PropertyValues.IsComputed(inputs, LightSleep)
            && !PropertyValues.IsComputed(inputs, DeepSleep)
        )
            failures.Add(
                new CheckFailure(LightSleep, "Set lightSleepDelaySeconds, deepSleepDelaySeconds or both.")
            );

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    public Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Task.FromResult(InputDiff.Compute(request, Inputs, ReplaceInputs, RequiredInputs));

    public async Task<CreateResponse> Create(CreateRequest request, CancellationToken ct)
    {
        var inputs = request.Inputs;
        if (request.Preview)
            return new CreateResponse { Outputs = PreviewOutputs(inputs) };

        var serviceId = PropertyValues.GetString(inputs, ServiceId)!;
        var deploymentId = await ApplyAsync(serviceId, inputs, request.Timeout, ct);
        return new CreateResponse
        {
            Id = serviceId,
            Outputs = Outputs(inputs, deploymentId),
        };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var api = client();
        var service = await api.GetServiceAsync(request.Id, ct);
        if (service is null)
            return new ReadResponse();

        var deploymentId = service["latest_deployment_id"]?.GetValue<string>();
        var definition = deploymentId is null
            ? null
            : (await api.GetDeploymentAsync(deploymentId, ct))["definition"] as JsonObject;
        var (light, deep) = definition is null ? (null, null) : LiveDelays(definition);

        var inputs = request.Inputs.IsEmpty
            ? ImmutableDictionary<string, PropertyValue>.Empty.Add(
                ServiceId,
                new PropertyValue(request.Id)
            )
            : request.Inputs;
        // Report drift for the delays this resource manages.
        if (!PropertyValues.Get(inputs, LightSleep).IsNull || request.Inputs.IsEmpty)
            inputs = inputs.SetItem(LightSleep, PropertyValues.Number(light));
        if (!PropertyValues.Get(inputs, DeepSleep).IsNull || request.Inputs.IsEmpty)
            inputs = inputs.SetItem(DeepSleep, PropertyValues.Number(deep));

        return new ReadResponse
        {
            Id = request.Id,
            Inputs = inputs,
            Outputs = Outputs(inputs, deploymentId),
        };
    }

    public async Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        if (request.Preview)
            return new UpdateResponse { Outputs = PreviewOutputs(inputs) };

        var deploymentId = await ApplyAsync(request.Id, inputs, request.Timeout, ct);
        return new UpdateResponse { Outputs = Outputs(inputs, deploymentId) };
    }

    // The delays belong to the service's definition; removing this resource stops managing them.
    public Task Delete(DeleteRequest request, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Sets the delays on the latest definition when they differ, redeploys and waits until the
    /// new deployment runs. Returns the deployment that carries the delays.
    /// </summary>
    private async Task<string> ApplyAsync(
        string serviceId,
        ImmutableDictionary<string, PropertyValue> inputs,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        var api = client();
        var service =
            await api.GetServiceAsync(serviceId, ct)
            ?? throw new InvalidOperationException($"Koyeb service {serviceId} does not exist.");
        var deploymentId =
            service["latest_deployment_id"]?.GetValue<string>()
            ?? throw new InvalidOperationException(
                $"Koyeb service {serviceId} has no deployment yet."
            );
        var definition =
            (await api.GetDeploymentAsync(deploymentId, ct))["definition"]?.DeepClone() as JsonObject
            ?? throw new InvalidOperationException(
                $"Koyeb deployment {deploymentId} has no definition."
            );

        var light = (long?)PropertyValues.GetNumber(inputs, LightSleep);
        var deep = (long?)PropertyValues.GetNumber(inputs, DeepSleep);
        if (definition["scalings"] is not JsonArray { Count: > 0 } scalings)
            throw new InvalidOperationException(
                $"Koyeb service {serviceId} has no scaling settings to add sleep delays to."
            );
        var live = LiveDelays(definition);
        if ((light is null || light == live.Light) && (deep is null || deep == live.Deep))
            return deploymentId;

        foreach (var scaling in scalings.OfType<JsonObject>())
        {
            var targets = scaling["targets"] as JsonArray ?? [];
            var kept = targets
                .OfType<JsonObject>()
                .Where(target => target["sleep_idle_delay"] is null)
                .Select(target => (JsonNode)target.DeepClone())
                .ToList();
            var sleep = new JsonObject();
            if ((light ?? live.Light) is { } lightValue)
                sleep["light_sleep_value"] = lightValue;
            if ((deep ?? live.Deep) is { } deepValue)
                sleep["deep_sleep_value"] = deepValue;
            kept.Add(new JsonObject { ["sleep_idle_delay"] = sleep });
            scaling["targets"] = new JsonArray([.. kept]);
        }

        var updated = await api.UpdateDefinitionAsync(serviceId, definition, ct);
        var newDeployment =
            updated["latest_deployment_id"]?.GetValue<string>()
            ?? throw new InvalidOperationException(
                $"Koyeb did not start a deployment for service {serviceId}."
            );

        await new VerificationPoller(delay).WaitAsync(
            token => api.GetDeploymentAsync(newDeployment, token),
            deployment => Running.Contains(Status(deployment)),
            deployment =>
                Failed.Contains(Status(deployment))
                    ? $"Koyeb deployment {newDeployment} of service {serviceId} ended {Status(deployment)}; check its logs in the Koyeb console."
                    : null,
            (deployment, budget) =>
                $"Koyeb deployment {newDeployment} of service {serviceId} was still {Status(deployment)} after {budget.TotalMinutes:0.#} minutes.",
            wait: true,
            timeout,
            ct
        );
        return newDeployment;
    }

    private static string Status(JsonObject deployment) =>
        deployment["status"]?.GetValue<string>() ?? "";

    private static (long? Light, long? Deep) LiveDelays(JsonObject definition)
    {
        var sleep = (definition["scalings"] as JsonArray)
            ?.OfType<JsonObject>()
            .SelectMany(scaling => (scaling["targets"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Select(target => target["sleep_idle_delay"] as JsonObject)
            .FirstOrDefault(value => value is not null);
        return (Number(sleep?["light_sleep_value"]), Number(sleep?["deep_sleep_value"] ?? sleep?["value"]));

        // Koyeb's API encodes int64 values as strings or numbers.
        static long? Number(JsonNode? node) =>
            node is JsonValue value
                ? value.TryGetValue<long>(out var number) ? number
                : value.TryGetValue<string>(out var text) && long.TryParse(text, out number) ? number
                : null
                : null;
    }

    private static ImmutableDictionary<string, PropertyValue> Outputs(
        ImmutableDictionary<string, PropertyValue> inputs,
        string? deploymentId
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(ServiceId, PropertyValues.Get(inputs, ServiceId))
            .Add(LightSleep, PropertyValues.Get(inputs, LightSleep))
            .Add(DeepSleep, PropertyValues.Get(inputs, DeepSleep))
            .Add(Triggers, PropertyValues.Get(inputs, Triggers))
            .Add("deploymentId", PropertyValues.String(deploymentId));

    private static ImmutableDictionary<string, PropertyValue> PreviewOutputs(
        ImmutableDictionary<string, PropertyValue> inputs
    ) => Outputs(inputs, null).SetItem("deploymentId", PropertyValue.Computed);
}
