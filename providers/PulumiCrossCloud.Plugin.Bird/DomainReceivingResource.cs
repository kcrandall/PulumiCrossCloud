using System.Collections.Immutable;
using System.Net;
using System.Text.Json.Nodes;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Bird;

/// <summary>
/// <c>crosscloud-bird:index:DomainReceiving</c>: enables inbound mail on a sending domain.
/// Bird requires the domain's DKIM record to be verified first, so create it after
/// <see cref="DomainVerificationResource"/>. Deleting it turns receiving off and waits until Bird
/// reports it off, because a domain that still owns receiving cannot be deleted.
/// </summary>
internal sealed class DomainReceivingResource(
    Func<BirdApiClient> client,
    Func<TimeSpan, CancellationToken, Task> delay
) : IResourceHandler
{
    public const string Type = "crosscloud-bird:index:DomainReceiving";

    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private const string DomainId = "domainId";
    private static readonly string[] Inputs = [DomainId];
    private static readonly IReadOnlySet<string> ReplaceInputs = new HashSet<string> { DomainId };

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        List<CheckFailure> failures = [];
        if (
            !PropertyValues.IsComputed(request.NewInputs, DomainId)
            && string.IsNullOrWhiteSpace(PropertyValues.GetString(request.NewInputs, DomainId))
        )
            failures.Add(new CheckFailure(DomainId, "domainId is required."));

        return Task.FromResult(
            new CheckResponse { Inputs = request.NewInputs, Failures = failures }
        );
    }

    public Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Task.FromResult(InputDiff.Compute(request, Inputs, ReplaceInputs, ReplaceInputs));

    public async Task<CreateResponse> Create(CreateRequest request, CancellationToken ct)
    {
        var domainId = PropertyValues.GetString(request.Inputs, DomainId);
        if (request.Preview)
            return new CreateResponse
            {
                Outputs = ImmutableDictionary<string, PropertyValue>
                    .Empty.Add(DomainId, PropertyValues.Get(request.Inputs, DomainId))
                    .Add("receivingState", PropertyValue.Computed)
                    .Add("inboundStatus", PropertyValue.Computed),
            };

        BirdDomain domain;
        try
        {
            domain = await client().UpdateDomainAsync(domainId!, SetInbound(enabled: true), ct);
        }
        catch (ApiException error)
            when (error.StatusCode == HttpStatusCode.UnprocessableEntity && error.Code == "E05019")
        {
            throw new InvalidOperationException(
                $"Bird can only enable receiving on {domainId} after its DKIM record is verified. "
                    + "Make DomainReceiving depend on DomainVerification.",
                error
            );
        }

        return new CreateResponse { Id = $"{domainId}/receiving", Outputs = ToOutputs(domain) };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var domainId = DomainIdOf(request.Inputs, request.Outputs, request.Id);
        var domain = await client().GetDomainAsync(domainId, ct);
        // Receiving turned off outside Pulumi reads as deleted, so the next update re-enables it.
        if (domain is null || IsOff(domain))
            return new ReadResponse();

        var inputs = request.Inputs.IsEmpty
            ? ImmutableDictionary<string, PropertyValue>.Empty.Add(
                DomainId,
                new PropertyValue(domainId)
            )
            : request.Inputs;
        return new ReadResponse
        {
            Id = request.Id,
            Outputs = ToOutputs(domain),
            Inputs = inputs,
        };
    }

    public async Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct)
    {
        if (request.Preview)
            return new UpdateResponse { Outputs = request.OldOutputs };

        var domain = await client()
            .GetDomainAsync(DomainIdOf(request.NewInputs, request.OldOutputs, request.Id), ct);
        return new UpdateResponse
        {
            Outputs = domain is null ? request.OldOutputs : ToOutputs(domain),
        };
    }

    public async Task Delete(DeleteRequest request, CancellationToken ct)
    {
        var domainId = DomainIdOf(request.OldInputs, request.OldOutputs, request.Id);
        var api = client();
        var budget = request.Timeout > TimeSpan.Zero ? request.Timeout : DefaultTimeout;
        var waited = TimeSpan.Zero;
        var disable = true;
        while (true)
        {
            BirdDomain? domain;
            try
            {
                domain = disable
                    ? await api.UpdateDomainAsync(domainId, SetInbound(enabled: false), ct)
                    : await api.GetDomainAsync(domainId, ct);
            }
            catch (ApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            if (domain is null || IsOff(domain))
                return;
            if (waited >= budget)
                throw new TimeoutException(
                    $"Bird did not finish turning off receiving for {domain.Domain} ({domainId}) "
                        + $"within {budget.TotalMinutes:0.#} minutes (state: {domain.Receiving?.State})."
                );

            // A failed turn-off can be retried by disabling again.
            disable = domain.Receiving?.State == "disable_failed";
            await delay(PollInterval, ct);
            waited += PollInterval;
        }
    }

    // Bird's documented signal that a domain no longer owns receiving (and can be deleted) is
    // inbound status not_configured; a turn-off still in flight or failed is not off yet.
    private static bool IsOff(BirdDomain domain) =>
        domain.Capabilities?.Inbound?.Status is null or "not_configured"
        && domain.Receiving?.State is not ("disabling" or "disable_failed");

    private static JsonObject SetInbound(bool enabled) =>
        new() { ["inbound"] = new JsonObject { ["enabled"] = enabled } };

    private static string DomainIdOf(
        ImmutableDictionary<string, PropertyValue> inputs,
        ImmutableDictionary<string, PropertyValue> outputs,
        string id
    ) =>
        PropertyValues.GetString(inputs, DomainId)
        ?? PropertyValues.GetString(outputs, DomainId)
        ?? id.Split('/')[0];

    private static ImmutableDictionary<string, PropertyValue> ToOutputs(BirdDomain domain) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(DomainId, new PropertyValue(domain.Id))
            .Add("receivingState", PropertyValues.String(domain.Receiving?.State))
            .Add("inboundStatus", PropertyValues.String(domain.Capabilities?.Inbound?.Status));
}
