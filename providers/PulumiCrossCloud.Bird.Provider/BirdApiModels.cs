namespace PulumiCrossCloud.Bird.Provider;

/// <summary>
/// The subset of Bird's sending-domain object that the provider manages or reports.
/// See https://bird.com/docs/api/reference/get-domain.
/// </summary>
internal sealed record BirdDomain
{
    public string Id { get; init; } = "";
    public string Domain { get; init; } = "";
    public string? WorkspaceId { get; init; }
    public string Status { get; init; } = "";
    public BirdDomainSettings? Settings { get; init; }
    public BirdDkim? Dkim { get; init; }
    public BirdCapabilities? Capabilities { get; init; }
    public List<BirdDnsRecord> DnsRecords { get; init; } = [];
    public string? VerifiedAt { get; init; }
    public string? LastCheckedAt { get; init; }
}

internal sealed record BirdDomainSettings
{
    public bool? ClickTracking { get; init; }
    public bool? OpenTracking { get; init; }
}

internal sealed record BirdDkim
{
    public string? Mode { get; init; }
    public string? Selector { get; init; }
}

internal sealed record BirdCapabilities
{
    public BirdCapability? Sending { get; init; }
    public BirdCapability? ReturnPath { get; init; }
    public BirdCapability? Dmarc { get; init; }
    public BirdCapability? Tracking { get; init; }
}

internal sealed record BirdCapability
{
    public string? Status { get; init; }
    public string? Domain { get; init; }
    public BirdPendingCapability? Pending { get; init; }
}

internal sealed record BirdPendingCapability
{
    public string? Domain { get; init; }
    public string? Status { get; init; }
}

internal sealed record BirdDnsRecord
{
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";
    public string Host { get; init; } = "";
    public string Value { get; init; } = "";
    public string Purpose { get; init; } = "";
    public string State { get; init; } = "";
    public bool Optional { get; init; }
    public string Status { get; init; } = "";
    public string? Error { get; init; }
}

internal sealed record BirdErrorEnvelope
{
    public BirdError? Error { get; init; }
}

internal sealed record BirdError
{
    public string? Type { get; init; }
    public string? Code { get; init; }
    public string? Message { get; init; }
    public string? RequestId { get; init; }
}
