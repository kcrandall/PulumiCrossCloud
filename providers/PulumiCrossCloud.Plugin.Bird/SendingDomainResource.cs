using System.Collections.Immutable;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pulumi.Experimental;
using Pulumi.Experimental.Provider;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Bird;

/// <summary>
/// <c>crosscloud-bird:index:SendingDomain</c>: a Bird email sending domain and the DNS records it needs.
/// Changing <c>domain</c> replaces the resource; name parts and tracking toggles update in place.
/// An empty <c>trackingName</c> removes the tracking domain that Bird adds by default, and an
/// empty <c>defaultIpPoolId</c> returns the domain to normal organization routing.
/// </summary>
internal sealed partial class SendingDomainResource(Func<BirdApiClient> client, Func<string> region)
    : IResourceHandler
{
    public const string Type = "crosscloud-bird:index:SendingDomain";

    private const string Domain = "domain";
    private const string ReturnPathName = "returnPathName";
    private const string TrackingName = "trackingName";
    private const string ClickTracking = "clickTracking";
    private const string OpenTracking = "openTracking";
    private const string DefaultIpPoolId = "defaultIpPoolId";

    private static readonly string[] Inputs =
    [
        Domain,
        ReturnPathName,
        TrackingName,
        ClickTracking,
        OpenTracking,
        DefaultIpPoolId,
    ];

    private static readonly string[] ComputedOutputs =
    [
        "workspaceId",
        "status",
        "sendingStatus",
        "returnPathDomain",
        "trackingDomain",
        "dkimSelector",
        "verifiedAt",
        "receivingState",
        "inboundStatus",
        "dnsRecords",
    ];

    private static readonly IReadOnlySet<string> ReplaceInputs = new HashSet<string> { Domain };
    private static readonly IReadOnlySet<string> RequiredInputs = new HashSet<string> { Domain };

    public Task<CheckResponse> Check(CheckRequest request, CancellationToken ct)
    {
        var inputs = request.NewInputs;
        List<CheckFailure> failures = [];

        if (!PropertyValues.IsComputed(inputs, Domain))
        {
            var domain = PropertyValues.GetString(inputs, Domain);
            if (string.IsNullOrWhiteSpace(domain))
                failures.Add(new CheckFailure(Domain, "domain is required."));
            else if (!HostnamePattern().IsMatch(domain))
                failures.Add(
                    new CheckFailure(
                        Domain,
                        $"'{domain}' is not a valid lowercase hostname such as mail.example.com."
                    )
                );
        }

        var returnPath = PropertyValues.GetString(inputs, ReturnPathName);
        if (returnPath is not null && !LabelPattern().IsMatch(returnPath))
            failures.Add(
                new CheckFailure(ReturnPathName, $"'{returnPath}' is not a valid DNS label.")
            );

        var tracking = PropertyValues.GetString(inputs, TrackingName);
        if (tracking is { Length: > 0 } && !LabelPattern().IsMatch(tracking))
            failures.Add(new CheckFailure(TrackingName, $"'{tracking}' is not a valid DNS label."));

        if (tracking == "")
        {
            foreach (var toggle in new[] { ClickTracking, OpenTracking })
            {
                if (PropertyValues.GetBool(inputs, toggle) == true)
                    failures.Add(
                        new CheckFailure(
                            toggle,
                            $"{toggle} requires a tracking domain, but trackingName is empty."
                        )
                    );
            }
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

        var domainName = PropertyValues.GetString(inputs, Domain)!;
        var body = new JsonObject { ["domain"] = domainName };
        if (PropertyValues.GetString(inputs, ReturnPathName) is { } returnPath)
            body["return_path"] = new JsonObject { ["name"] = returnPath };
        var tracking = PropertyValues.GetString(inputs, TrackingName);
        if (tracking is { Length: > 0 })
            body["tracking"] = new JsonObject { ["name"] = tracking };
        if (SettingsPatch(PropertyValues.Empty, inputs) is { } settings)
            body["settings"] = settings;

        var api = client();
        BirdDomain domain;
        try
        {
            domain = await api.CreateDomainAsync(body, ct);
        }
        catch (ApiException error) when (error.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                $"Bird already has a sending domain named '{domainName}' in this workspace. "
                    + $"Adopt it with `pulumi import {Type} <resource-name> <dom_id>`, or delete it in Bird and retry. "
                    + error.Message,
                error
            );
        }

        // Settings Bird only accepts on update: removing the default tracking domain it assigns
        // at creation, and the default IP pool.
        var followUp = new JsonObject();
        if (tracking == "")
            followUp["tracking"] = null;
        if (PropertyValues.GetString(inputs, DefaultIpPoolId) is { Length: > 0 } pool)
            followUp["default_ip_pool_id"] = pool;
        if (followUp.Count > 0)
        {
            try
            {
                domain = await api.UpdateDomainAsync(domain.Id, followUp, ct);
            }
            catch
            {
                await api.DeleteDomainAsync(domain.Id, CancellationToken.None);
                throw;
            }
        }

        return new CreateResponse { Id = domain.Id, Outputs = ToOutputs(domain) };
    }

    public async Task<ReadResponse> Read(ReadRequest request, CancellationToken ct)
    {
        var domain = await client().GetDomainAsync(request.Id, ct);
        if (domain is null)
            return new ReadResponse();

        var outputs = ToOutputs(domain);
        var inputs = request.Inputs.IsEmpty
            // Import: adopt every managed setting from the live domain.
            ? Inputs.ToImmutableDictionary(key => key, key => outputs[key])
            // Refresh: report drift only for settings the program manages.
            : request.Inputs.SetItems(
                Inputs
                    .Where(key => !PropertyValues.Get(request.Inputs, key).IsNull)
                    .Select(key => KeyValuePair.Create(key, outputs[key]))
            );

        return new ReadResponse
        {
            Id = domain.Id,
            Outputs = outputs,
            Inputs = inputs,
        };
    }

    public async Task<UpdateResponse> Update(UpdateRequest request, CancellationToken ct)
    {
        var olds = request.OldInputs.IsEmpty ? request.OldOutputs : request.OldInputs;
        var news = request.NewInputs;
        var dnsChanges =
            InputDiff.Changed(olds, news, ReturnPathName)
            || InputDiff.Changed(olds, news, TrackingName);

        if (request.Preview)
        {
            var preview = request.OldOutputs.SetItems(
                Inputs
                    .Where(key => !PropertyValues.Get(news, key).IsNull)
                    .Select(key => KeyValuePair.Create(key, PropertyValues.Get(news, key)))
            );
            if (dnsChanges)
                preview = preview.SetItems(
                    ["returnPathDomain", "trackingDomain", "dnsRecords", "sendingStatus"],
                    PropertyValue.Computed
                );
            return new UpdateResponse { Outputs = preview };
        }

        var patch = new JsonObject();
        if (InputDiff.Changed(olds, news, ReturnPathName))
            patch["return_path"] = new JsonObject
            {
                ["name"] = PropertyValues.GetString(news, ReturnPathName),
            };
        if (InputDiff.Changed(olds, news, TrackingName))
        {
            var tracking = PropertyValues.GetString(news, TrackingName);
            patch["tracking"] = tracking == "" ? null : new JsonObject { ["name"] = tracking };
        }
        if (SettingsPatch(olds, news) is { } settings)
            patch["settings"] = settings;
        if (InputDiff.Changed(olds, news, DefaultIpPoolId))
        {
            var pool = PropertyValues.GetString(news, DefaultIpPoolId);
            patch["default_ip_pool_id"] = pool == "" ? null : pool;
        }

        var api = client();
        var domain =
            patch.Count == 0
                ? await api.GetDomainAsync(request.Id, ct)
                : await api.UpdateDomainAsync(request.Id, patch, ct);
        if (domain is null)
            throw new InvalidOperationException(
                $"Bird sending domain {request.Id} no longer exists. Run `pulumi refresh` to recreate it."
            );

        return new UpdateResponse { Outputs = ToOutputs(domain) };
    }

    public Task Delete(DeleteRequest request, CancellationToken ct) =>
        client().DeleteDomainAsync(request.Id, ct);

    internal ImmutableDictionary<string, PropertyValue> ToOutputs(BirdDomain domain)
    {
        var returnPathDomain = ConfiguredHost(domain.Capabilities?.ReturnPath);
        var trackingDomain = ConfiguredHost(domain.Capabilities?.Tracking);
        var records = domain.DnsRecords.Select(record => new PropertyValue(
            ImmutableDictionary<string, PropertyValue>
                .Empty.Add("type", new PropertyValue(record.Type))
                .Add("name", new PropertyValue(record.Name))
                .Add("host", new PropertyValue(record.Host))
                .Add("value", new PropertyValue(record.Value))
                .Add("purpose", new PropertyValue(record.Purpose))
                .Add("state", new PropertyValue(record.State))
                .Add("optional", new PropertyValue(record.Optional))
                .Add("status", new PropertyValue(record.Status))
        ));

        return ImmutableDictionary<string, PropertyValue>
            .Empty.Add(Domain, new PropertyValue(domain.Domain))
            .Add("region", new PropertyValue(region()))
            .Add("workspaceId", PropertyValues.String(domain.WorkspaceId))
            .Add("status", new PropertyValue(domain.Status))
            .Add("sendingStatus", PropertyValues.String(domain.Capabilities?.Sending?.Status))
            .Add(ReturnPathName, PropertyValues.String(NamePart(returnPathDomain, domain.Domain)))
            .Add("returnPathDomain", PropertyValues.String(returnPathDomain))
            .Add(TrackingName, new PropertyValue(NamePart(trackingDomain, domain.Domain) ?? ""))
            .Add("trackingDomain", PropertyValues.String(trackingDomain))
            .Add(ClickTracking, new PropertyValue(domain.Settings?.ClickTracking ?? false))
            .Add(OpenTracking, new PropertyValue(domain.Settings?.OpenTracking ?? false))
            .Add("dkimSelector", PropertyValues.String(domain.Dkim?.Selector))
            .Add("verifiedAt", PropertyValues.String(domain.VerifiedAt))
            // An empty string, like the input, means normal organization routing.
            .Add(DefaultIpPoolId, new PropertyValue(domain.DefaultIpPoolId ?? ""))
            .Add("receivingState", PropertyValues.String(domain.Receiving?.State))
            .Add("inboundStatus", PropertyValues.String(domain.Capabilities?.Inbound?.Status))
            .Add("dnsRecords", new PropertyValue([.. records]));
    }

    private ImmutableDictionary<string, PropertyValue> PreviewOutputs(
        ImmutableDictionary<string, PropertyValue> inputs
    )
    {
        var outputs = ImmutableDictionary<string, PropertyValue>.Empty.Add(
            "region",
            new PropertyValue(region())
        );
        foreach (var key in Inputs)
        {
            var value = PropertyValues.Get(inputs, key);
            outputs = outputs.Add(key, value.IsNull ? PropertyValue.Computed : value);
        }

        return outputs.SetItems(ComputedOutputs, PropertyValue.Computed);
    }

    /// <summary>
    /// Settings changes from <paramref name="olds"/> to <paramref name="news"/>, or null when none.
    /// </summary>
    private static JsonObject? SettingsPatch(
        ImmutableDictionary<string, PropertyValue> olds,
        ImmutableDictionary<string, PropertyValue> news
    )
    {
        var settings = new JsonObject();
        if (InputDiff.Changed(olds, news, ClickTracking))
            settings["click_tracking"] = PropertyValues.GetBool(news, ClickTracking);
        if (InputDiff.Changed(olds, news, OpenTracking))
            settings["open_tracking"] = PropertyValues.GetBool(news, OpenTracking);
        return settings.Count == 0 ? null : settings;
    }

    /// <summary>
    /// The hostname a capability is configured with, preferring a staged change awaiting DNS.
    /// </summary>
    private static string? ConfiguredHost(BirdCapability? capability) =>
        capability?.Status == "not_configured" && capability.Pending is null
            ? null
            : capability?.Pending?.Domain ?? capability?.Domain;

    private static string? NamePart(string? host, string domain) =>
        host is null ? null
        : host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase)
            ? host[..^(domain.Length + 1)]
        : host;

    [GeneratedRegex(
        @"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,61}[a-z0-9]$"
    )]
    private static partial Regex HostnamePattern();

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex LabelPattern();
}
