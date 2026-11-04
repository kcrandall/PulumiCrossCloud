using System.Collections.Immutable;
using System.Net;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Mailgun;

/// <summary>
/// <c>crosscloud-mailgun:index:DomainVerification</c>: asks Mailgun to check a domain's DNS
/// records and, by default, waits until the domain is active. Create it after the records are
/// published. Deleting it has no effect in Mailgun. Changing <c>domain</c> or <c>triggers</c>
/// re-runs verification.
/// </summary>
internal sealed class DomainVerificationResource(
    Func<MailgunApiClient> client,
    Func<TimeSpan, CancellationToken, Task> delay
) : IResourceHandler
{
    public const string Type = "crosscloud-mailgun:index:DomainVerification";

    private const string Domain = "domain";
    private const string WaitForSending = "waitForSending";
    private const string Triggers = "triggers";

    private static readonly string[] Inputs = [Domain, WaitForSending, Triggers];
    private static readonly IReadOnlySet<string> ReplaceInputs = new HashSet<string>
    {
        Domain,
        Triggers,
    };
    private static readonly IReadOnlySet<string> RequiredInputs = new HashSet<string> { Domain };

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        if (
            !PropertyValues.IsComputed(inputs, Domain)
            && string.IsNullOrWhiteSpace(PropertyValues.GetString(inputs, Domain))
        )
            failures.Add(new CheckFailure(Domain, "domain is required."));

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

        var domain = PropertyValues.GetString(inputs, Domain)!;
        var result = await VerifyAsync(domain, ShouldWait(inputs), request.Timeout, ct);
        return new CreateResponse
        {
            Id = $"{domain}/verification",
            Outputs = ToOutputs(inputs, result),
        };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var domain =
            PropertyValues.GetString(request.Inputs, Domain)
            ?? PropertyValues.GetString(request.Outputs, Domain)
            ?? request.Id.Split('/')[0];
        var result = await client().GetDomainAsync(domain, ct);
        if (result is null)
            return new ReadResponse();

        var inputs = request.Inputs.IsEmpty
            ? ImmutableDictionary<string, PropertyValue>.Empty.Add(
                Domain,
                new PropertyValue(domain)
            )
            : request.Inputs;
        return new ReadResponse
        {
            Id = request.Id,
            Outputs = ToOutputs(inputs, result),
            Inputs = inputs,
        };
    }

    public async Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        if (request.Preview)
            return new UpdateResponse { Outputs = PreviewOutputs(inputs) };

        var domain = PropertyValues.GetString(inputs, Domain)!;
        var result = await VerifyAsync(domain, ShouldWait(inputs), request.Timeout, ct);
        return new UpdateResponse { Outputs = ToOutputs(inputs, result) };
    }

    public Task Delete(DeleteRequest request, CancellationToken ct) => Task.CompletedTask;

    internal Task<MailgunDomainResponse> VerifyAsync(
        string domain,
        bool wait,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        var api = client();
        return new VerificationPoller(delay).WaitAsync(
            async token =>
            {
                try
                {
                    return await api.VerifyDomainAsync(domain, token);
                }
                catch (ApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException(
                        $"Mailgun domain {domain} does not exist.",
                        error
                    );
                }
            },
            result => result.Domain.State == "active",
            result =>
                result.Domain.State == "disabled" || result.Domain.IsDisabled
                    ? $"Mailgun has disabled {domain}; check the domain in the Mailgun dashboard."
                    : null,
            DescribePending,
            wait,
            timeout,
            ct
        );
    }

    private static bool ShouldWait(ImmutableDictionary<string, PropertyValue> inputs) =>
        PropertyValues.GetBool(inputs, WaitForSending) ?? true;

    private static string DescribePending(MailgunDomainResponse result, TimeSpan budget)
    {
        var records = result
            .SendingDnsRecords.Where(record => record.Valid != "valid")
            .Select(record =>
                $"  - {record.RecordType} {record.Name ?? result.Domain.Name}: {record.Valid} (expected {record.Value})"
            );
        return $"Mailgun domain {result.Domain.Name} was not active after {budget.TotalMinutes:0.#} minutes "
            + $"(state: {result.Domain.State}). Check that these DNS records are published, then run `pulumi up` again:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, records);
    }

    private static ImmutableDictionary<string, PropertyValue> ToOutputs(
        ImmutableDictionary<string, PropertyValue> inputs,
        MailgunDomainResponse result
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(Domain, new PropertyValue(result.Domain.Name))
            .Add(WaitForSending, new PropertyValue(ShouldWait(inputs)))
            .Add(Triggers, PropertyValues.Get(inputs, Triggers))
            .Add("state", new PropertyValue(result.Domain.State));

    private static ImmutableDictionary<string, PropertyValue> PreviewOutputs(
        ImmutableDictionary<string, PropertyValue> inputs
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(Domain, PropertyValues.Get(inputs, Domain))
            .Add(WaitForSending, new PropertyValue(ShouldWait(inputs)))
            .Add(Triggers, PropertyValues.Get(inputs, Triggers))
            .Add("state", PropertyValue.Computed);
}
