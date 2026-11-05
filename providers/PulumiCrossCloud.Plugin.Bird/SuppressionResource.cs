using System.Collections.Immutable;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Bird;

/// <summary>
/// <c>crosscloud-bird:index:Suppression</c>: a manual suppression that stops all email to one
/// address. Changing the address replaces it; deleting it removes only this manual record, so
/// bounce or complaint suppressions for the address stay in effect.
/// </summary>
internal sealed class SuppressionResource(Func<BirdApiClient> client) : IResourceHandler
{
    public const string Type = "crosscloud-bird:index:Suppression";

    private const string Email = "email";

    private static readonly string[] Inputs = [Email];
    private static readonly IReadOnlySet<string> Required = new HashSet<string> { Email };

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        var email = PropertyValues.GetString(inputs, Email);
        if (!PropertyValues.IsComputed(inputs, Email) && (email is null || !email.Contains('@')))
            failures.Add(new CheckFailure(Email, "email must be an email address."));
        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    public Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Task.FromResult(InputDiff.Compute(request, Inputs, Required, Required));

    public async Task<CreateResponse> Create(CreateRequest request, CancellationToken ct)
    {
        if (request.Preview)
            return new CreateResponse
            {
                Outputs = request.Inputs.SetItem("reason", new PropertyValue("manual")),
            };

        var suppression = await client()
            .CreateSuppressionAsync(PropertyValues.GetString(request.Inputs, Email)!, ct);
        return new CreateResponse { Id = suppression.Id, Outputs = ToOutputs(suppression) };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var suppression = await client().GetSuppressionAsync(request.Id, ct);
        return suppression is null
            ? new ReadResponse()
            : new ReadResponse
            {
                Id = suppression.Id,
                Inputs = ImmutableDictionary<string, PropertyValue>.Empty.Add(
                    Email,
                    new PropertyValue(suppression.Email)
                ),
                Outputs = ToOutputs(suppression),
            };
    }

    public Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("Suppressions are replaced, never updated.");

    public async Task Delete(DeleteRequest request, CancellationToken ct) =>
        await client().DeleteSuppressionAsync(request.Id, ct);

    private static ImmutableDictionary<string, PropertyValue> ToOutputs(
        BirdSuppression suppression
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(Email, new PropertyValue(suppression.Email))
            .Add("reason", new PropertyValue(suppression.Reason));
}
