using System.Collections.Immutable;
using System.Text.Json;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Cloudflare;

/// <summary>
/// <c>crosscloud-cloudflare:index:AccountToken</c>: an account-owned Cloudflare API token with a
/// single allow policy. The Terraform-bridged <c>cloudflare</c> provider's token resource creates
/// user tokens, which stop working when that user leaves the account. Name and policy changes
/// update the token in place and keep its value. Changing the account replaces it, and deleting
/// it revokes the token.
/// </summary>
internal sealed class AccountTokenResource(Func<CloudflareApiClient> client) : IResourceHandler
{
    public const string Type = "crosscloud-cloudflare:index:AccountToken";

    private const string AccountId = "accountId";
    private const string Name = "name";
    private const string PermissionGroupIds = "permissionGroupIds";
    private const string Resources = "resources";
    private const string Value = "value";
    private const string Status = "status";

    private static readonly string[] Inputs = [AccountId, Name, PermissionGroupIds, Resources];
    private static readonly IReadOnlySet<string> ReplaceInputs = new HashSet<string>
    {
        AccountId,
    };
    private static readonly IReadOnlySet<string> RequiredInputs = new HashSet<string>(Inputs);

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];
        foreach (var key in new[] { AccountId, Name })
            if (
                !PropertyValues.IsComputed(inputs, key)
                && string.IsNullOrWhiteSpace(PropertyValues.GetString(inputs, key))
            )
                failures.Add(new CheckFailure(key, $"{key} is required."));

        var groups = PropertyValues.Get(inputs, PermissionGroupIds);
        if (!groups.IsComputed && (!groups.TryGetArray(out var items) || items.IsEmpty))
            failures.Add(
                new CheckFailure(PermissionGroupIds, "At least one permission group is required.")
            );

        var resources = PropertyValues.Get(inputs, Resources);
        if (!resources.IsComputed && (!resources.TryGetMap(out var map) || map.IsEmpty))
            failures.Add(new CheckFailure(Resources, "At least one resource is required."));

        return Task.FromResult(new CheckResponse { Inputs = inputs, Failures = failures });
    }

    public Task<DiffResponse> Diff(DiffRequest request, CancellationToken ct) =>
        Task.FromResult(InputDiff.Compute(request, Inputs, ReplaceInputs, RequiredInputs));

    public async Task<CreateResponse> Create(CreateRequest request, CancellationToken ct)
    {
        var inputs = request.Inputs;
        if (request.Preview)
            return new CreateResponse
            {
                Outputs = inputs
                    .SetItem(Value, PropertyValue.Computed.WithSecret(true))
                    .SetItem(Status, PropertyValue.Computed),
            };

        var accountId = PropertyValues.GetString(inputs, AccountId)!;
        var token = await client().CreateTokenAsync(accountId, Policy(inputs), ct);
        if (string.IsNullOrEmpty(token.Id) || string.IsNullOrEmpty(token.Value))
            // The response holds a new bearer token; never echo it into a diagnostic.
            throw new InvalidOperationException("Cloudflare returned an empty token.");

        return new CreateResponse { Id = token.Id, Outputs = ToOutputs(inputs, token, token.Value) };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var olds = request.Inputs.IsEmpty ? request.Outputs : request.Inputs;
        var accountId = PropertyValues.GetString(olds, AccountId);
        if (accountId is null)
            throw new InvalidOperationException(
                "Cloudflare account tokens cannot be imported: Cloudflare only returns a token's value when it is created."
            );

        var token = await client().GetTokenAsync(accountId, request.Id, ct);
        if (token is null)
            return new ReadResponse();

        var inputs = request.Inputs;
        if (!inputs.IsEmpty)
        {
            inputs = inputs.SetItem(Name, new PropertyValue(token.Name));
            if (token.Policies is [var policy] && policy.Effect == "allow")
                inputs = inputs
                    .SetItem(
                        PermissionGroupIds,
                        new PropertyValue(
                            [.. policy.PermissionGroups.Select(group => new PropertyValue(group.Id))]
                        )
                    )
                    .SetItem(
                        Resources,
                        new PropertyValue(
                            policy.Resources.ToImmutableDictionary(
                                pair => pair.Key,
                                pair => new PropertyValue(
                                    pair.Value.ValueKind == JsonValueKind.String
                                        ? pair.Value.GetString()!
                                        : pair.Value.GetRawText()
                                )
                            )
                        )
                    );
        }

        var value = PropertyValues.GetString(request.Outputs, Value);
        return new ReadResponse
        {
            Id = token.Id,
            Inputs = inputs,
            Outputs = ToOutputs(inputs.IsEmpty ? olds : inputs, token, value),
        };
    }

    public async Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        var value = PropertyValues.GetString(request.OldOutputs, Value);
        if (request.Preview)
            return new UpdateResponse
            {
                Outputs = inputs
                    .SetItem(Value, PropertyValues.Get(request.OldOutputs, Value).WithSecret(true))
                    .SetItem(Status, PropertyValues.Get(request.OldOutputs, Status)),
            };

        var accountId = PropertyValues.GetString(inputs, AccountId)!;
        var token = await client().UpdateTokenAsync(accountId, request.Id, Policy(inputs), ct);
        return new UpdateResponse { Outputs = ToOutputs(inputs, token, value) };
    }

    public async Task Delete(DeleteRequest request, CancellationToken ct)
    {
        var olds = request.OldInputs.IsEmpty ? request.OldOutputs : request.OldInputs;
        var accountId =
            PropertyValues.GetString(olds, AccountId)
            ?? throw new InvalidOperationException(
                $"Cannot revoke Cloudflare token {request.Id}: its account ID is not in state."
            );
        await client().DeleteTokenAsync(accountId, request.Id, ct);
    }

    private static TokenPolicy Policy(ImmutableDictionary<string, PropertyValue> inputs)
    {
        if (
            !PropertyValues.Get(inputs, PermissionGroupIds).TryGetArray(out var groups)
            || !PropertyValues.Get(inputs, Resources).TryGetMap(out var resources)
        )
            throw new ArgumentException("permissionGroupIds and resources are required.");
        return new TokenPolicy(
            PropertyValues.GetString(inputs, Name)!,
            [
                .. groups.Select(group =>
                    group.TryGetString(out var id)
                        ? id
                        : throw new ArgumentException("Permission group IDs must be strings.")
                ),
            ],
            resources.ToDictionary(
                pair => pair.Key,
                pair =>
                    pair.Value.TryGetString(out var access)
                        ? access
                        : throw new ArgumentException("Resource access levels must be strings.")
            )
        );
    }

    private static ImmutableDictionary<string, PropertyValue> ToOutputs(
        ImmutableDictionary<string, PropertyValue> inputs,
        CloudflareToken token,
        string? value
    ) =>
        ImmutableDictionary<string, PropertyValue>
            .Empty.Add(AccountId, PropertyValues.Get(inputs, AccountId))
            .Add(Name, new PropertyValue(token.Name))
            .Add(PermissionGroupIds, PropertyValues.Get(inputs, PermissionGroupIds))
            .Add(Resources, PropertyValues.Get(inputs, Resources))
            .Add(Status, new PropertyValue(token.Status))
            .Add(Value, PropertyValues.String(value).WithSecret(true));
}
