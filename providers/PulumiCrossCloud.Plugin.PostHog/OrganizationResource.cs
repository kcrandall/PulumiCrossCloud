using System.Collections.Immutable;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.PostHog;

/// <summary>
/// <c>crosscloud-posthog:index:Organization</c>: a PostHog organization. Creating it adopts an
/// existing organization with the same name instead of creating a duplicate. Renames update in
/// place. The organization is only deleted on destroy when <c>deleteOnDestroy</c> is true.
/// </summary>
internal sealed class OrganizationResource(Func<PostHogApiClient> client) : IResourceHandler
{
    public const string Type = "crosscloud-posthog:index:Organization";

    private const string Name = "name";
    private const string DeleteOnDestroy = "deleteOnDestroy";

    private static readonly string[] Inputs = [Name, DeleteOnDestroy];
    private static readonly IReadOnlySet<string> RequiredInputs = new HashSet<string> { Name };

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        List<CheckFailure> failures = [];
        if (
            !PropertyValues.IsComputed(request.NewInputs, Name)
            && string.IsNullOrWhiteSpace(PropertyValues.GetString(request.NewInputs, Name))
        )
            failures.Add(new CheckFailure(Name, "name is required."));

        return Task.FromResult(
            new CheckResponse { Inputs = request.NewInputs, Failures = failures }
        );
    }

    public Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Task.FromResult(InputDiff.Compute(request, Inputs, new HashSet<string>(), RequiredInputs));

    public async Task<CreateResponse> Create(CreateRequest request, CancellationToken ct)
    {
        var inputs = request.Inputs;
        if (request.Preview)
            return new CreateResponse
            {
                Outputs = ImmutableDictionary<string, PropertyValue>
                    .Empty.Add(Name, PropertyValues.Get(inputs, Name))
                    .Add(DeleteOnDestroy, new PropertyValue(ShouldDelete(inputs)))
                    .Add("slug", PropertyValue.Computed)
                    .Add("adopted", PropertyValue.Computed),
            };

        var name = PropertyValues.GetString(inputs, Name)!;
        var api = client();
        var existing = await api.FindOrganizationAsync(name, ct);
        var organization = existing ?? await api.CreateOrganizationAsync(name, ct);
        return new CreateResponse
        {
            Id = organization.Id,
            Outputs = ToOutputs(organization, inputs, adopted: existing is not null),
        };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var organization = await client().GetOrganizationAsync(request.Id, ct);
        if (organization is null)
            return new ReadResponse();

        var inputs = (
            request.Inputs.IsEmpty
                ? ImmutableDictionary<string, PropertyValue>.Empty.Add(
                    DeleteOnDestroy,
                    new PropertyValue(false)
                )
                : request.Inputs
        ).SetItem(Name, new PropertyValue(organization.Name));
        var adopted = PropertyValues.GetBool(request.Outputs, "adopted") ?? request.Inputs.IsEmpty;
        return new ReadResponse
        {
            Id = organization.Id,
            Outputs = ToOutputs(organization, inputs, adopted),
            Inputs = inputs,
        };
    }

    public async Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct)
    {
        var news = request.NewInputs;
        var adopted = PropertyValues.GetBool(request.OldOutputs, "adopted") ?? false;
        if (request.Preview)
            return new UpdateResponse
            {
                Outputs = request
                    .OldOutputs.SetItem(Name, PropertyValues.Get(news, Name))
                    .SetItem(DeleteOnDestroy, new PropertyValue(ShouldDelete(news))),
            };

        var olds = request.OldInputs.IsEmpty ? request.OldOutputs : request.OldInputs;
        var api = client();
        var organization = InputDiff.Changed(olds, news, Name)
            ? await api.RenameOrganizationAsync(
                request.Id,
                PropertyValues.GetString(news, Name)!,
                ct
            )
            : await api.GetOrganizationAsync(request.Id, ct)
                ?? throw new InvalidOperationException(
                    $"PostHog organization {request.Id} no longer exists. Run `pulumi refresh` to recreate it."
                );
        return new UpdateResponse { Outputs = ToOutputs(organization, news, adopted) };
    }

    public async Task Delete(DeleteRequest request, CancellationToken ct)
    {
        var inputs = request.OldInputs.IsEmpty ? request.OldOutputs : request.OldInputs;
        if (ShouldDelete(inputs))
            await client().DeleteOrganizationAsync(request.Id, ct);
    }

    private static bool ShouldDelete(ImmutableDictionary<string, PropertyValue> inputs) =>
        PropertyValues.GetBool(inputs, DeleteOnDestroy) ?? false;

    private static ImmutableDictionary<string, PropertyValue> ToOutputs(
        PostHogOrganization organization,
        ImmutableDictionary<string, PropertyValue> inputs,
        bool adopted
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(Name, new PropertyValue(organization.Name))
            .Add("slug", PropertyValues.String(organization.Slug))
            .Add(DeleteOnDestroy, new PropertyValue(ShouldDelete(inputs)))
            // Whether creation found an existing organization rather than creating one.
            .Add("adopted", new PropertyValue(adopted));
}
