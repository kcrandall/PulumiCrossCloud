using System.Collections.Immutable;
using System.Net;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;

namespace PulumiCrossCloud.Bird.Provider;

/// <summary>
/// <c>bird:index:DomainVerification</c>: triggers Bird's DNS check for a sending domain and,
/// by default, waits until the domain may send. Create it after the DNS records are published.
/// Deleting it has no effect in Bird. Changing <c>domainId</c> or <c>triggers</c> re-runs verification.
/// </summary>
internal sealed class DomainVerificationResource(
    Func<BirdApiClient> client,
    Func<TimeSpan, CancellationToken, Task> delay
) : IBirdResource
{
    public const string Type = "bird:index:DomainVerification";

    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan InitialPollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(30);

    private const string DomainId = "domainId";
    private const string WaitForSending = "waitForSending";
    private const string Triggers = "triggers";

    private static readonly string[] Inputs = [DomainId, WaitForSending, Triggers];
    private static readonly IReadOnlySet<string> ReplaceInputs = new HashSet<string>
    {
        DomainId,
        Triggers,
    };
    private static readonly IReadOnlySet<string> RequiredInputs = new HashSet<string> { DomainId };

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        if (
            !PropertyValues.IsComputed(inputs, DomainId)
            && string.IsNullOrWhiteSpace(PropertyValues.GetString(inputs, DomainId))
        )
            failures.Add(new CheckFailure(DomainId, "domainId is required."));

        var triggers = PropertyValues.Get(inputs, Triggers);
        if (!triggers.IsNull && !triggers.IsComputed && !triggers.TryGetArray(out _))
            failures.Add(new CheckFailure(Triggers, "triggers must be a list."));

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    public Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Task.FromResult(InputDiff.Compute(request, Inputs, ReplaceInputs, RequiredInputs));

    public async Task<CreateResponse> Create(CreateRequest request, CancellationToken ct)
    {
        var inputs = request.Inputs;
        if (request.Preview)
            return new CreateResponse { Outputs = PreviewOutputs(inputs) };

        var domainId = PropertyValues.GetString(inputs, DomainId)!;
        var domain = await VerifyAsync(domainId, ShouldWait(inputs), request.Timeout, ct);
        return new CreateResponse
        {
            Id = $"{domainId}/verification",
            Outputs = ToOutputs(inputs, domain),
        };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var domainId =
            PropertyValues.GetString(request.Inputs, DomainId)
            ?? PropertyValues.GetString(request.Outputs, DomainId)
            ?? request.Id.Split('/')[0];
        var domain = await client().GetDomainAsync(domainId, ct);
        if (domain is null)
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
            Outputs = ToOutputs(inputs, domain),
            Inputs = inputs,
        };
    }

    public async Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        if (request.Preview)
            return new UpdateResponse { Outputs = PreviewOutputs(inputs) };

        var domainId = PropertyValues.GetString(inputs, DomainId)!;
        var domain = await VerifyAsync(domainId, ShouldWait(inputs), request.Timeout, ct);
        return new UpdateResponse { Outputs = ToOutputs(inputs, domain) };
    }

    public Task Delete(DeleteRequest request, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Runs Bird's DNS check and, when <paramref name="wait"/> is set, repeats it with backoff
    /// until the sending capability is verified or <paramref name="timeout"/> elapses.
    /// </summary>
    internal async Task<BirdDomain> VerifyAsync(
        string domainId,
        bool wait,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        var budget = timeout > TimeSpan.Zero ? timeout : DefaultTimeout;
        var waited = TimeSpan.Zero;
        var interval = InitialPollInterval;
        var api = client();
        while (true)
        {
            BirdDomain domain;
            try
            {
                domain = await api.VerifyDomainAsync(domainId, ct);
            }
            catch (BirdApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException(
                    $"Bird sending domain {domainId} does not exist.",
                    error
                );
            }

            if (!wait || domain.Capabilities?.Sending?.Status == "verified")
                return domain;
            if (domain.Status == "rejected")
                throw new InvalidOperationException(
                    $"Bird rejected sending domain {domain.Domain} ({domain.Id}) for policy reasons; contact Bird support."
                );
            if (waited + interval > budget)
                throw new TimeoutException(DescribePending(domain, budget));

            await delay(interval, ct);
            waited += interval;
            interval = TimeSpan.FromTicks(
                Math.Min((long)(interval.Ticks * 1.5), MaxPollInterval.Ticks)
            );
        }
    }

    private static bool ShouldWait(ImmutableDictionary<string, PropertyValue> inputs) =>
        PropertyValues.GetBool(inputs, WaitForSending) ?? true;

    private static string DescribePending(BirdDomain domain, TimeSpan budget)
    {
        var records = domain
            .DnsRecords.Where(record =>
                !record.Optional && record.State != "deprecated" && record.Status != "verified"
            )
            .Select(record =>
                $"  - {record.Purpose} {record.Type} {record.Host}: {record.Status}"
                + (record.Error is null ? "" : $" ({record.Error})")
            );
        return $"Bird sending domain {domain.Domain} ({domain.Id}) was not ready to send after {budget.TotalMinutes:0.#} minutes "
            + $"(ownership: {domain.Status}, sending: {domain.Capabilities?.Sending?.Status ?? "unknown"}). "
            + "Check that these DNS records are published, then run `pulumi up` again:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, records);
    }

    private static ImmutableDictionary<string, PropertyValue> ToOutputs(
        ImmutableDictionary<string, PropertyValue> inputs,
        BirdDomain domain
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(DomainId, new PropertyValue(domain.Id))
            .Add(WaitForSending, new PropertyValue(ShouldWait(inputs)))
            .Add(Triggers, PropertyValues.Get(inputs, Triggers))
            .Add("status", new PropertyValue(domain.Status))
            .Add("sendingStatus", PropertyValues.String(domain.Capabilities?.Sending?.Status))
            .Add("verifiedAt", PropertyValues.String(domain.VerifiedAt));

    private static ImmutableDictionary<string, PropertyValue> PreviewOutputs(
        ImmutableDictionary<string, PropertyValue> inputs
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(DomainId, PropertyValues.Get(inputs, DomainId))
            .Add(WaitForSending, new PropertyValue(ShouldWait(inputs)))
            .Add(Triggers, PropertyValues.Get(inputs, Triggers))
            .Add("status", PropertyValue.Computed)
            .Add("sendingStatus", PropertyValue.Computed)
            .Add("verifiedAt", PropertyValue.Computed);
}
