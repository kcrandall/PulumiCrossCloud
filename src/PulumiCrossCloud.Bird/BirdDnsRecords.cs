namespace PulumiCrossCloud.Bird;

/// <summary>
/// Chooses which of Bird's reported DNS records to publish.
/// </summary>
public static class BirdDnsRecords
{
    public const string Dkim = "dkim";
    public const string ReturnPath = "return_path";
    public const string Dmarc = "dmarc";
    public const string Tracking = "tracking";
    public const string InboundMx = "inbound_mx";

    /// <summary>
    /// Fallback DMARC policy published when Bird does not suggest one. <c>p=none</c> satisfies Bird's sending check.
    /// </summary>
    public const string DefaultDmarcPolicy = "v=DMARC1; p=none;";

    /// <summary>
    /// The record to publish for <paramref name="purpose"/>: a staged (pending) change first,
    /// because the staged configuration only takes effect once its record verifies; otherwise
    /// the active record. Deprecated records are never selected.
    /// </summary>
    public static SendingDomainDnsRecord? Select(
        IEnumerable<SendingDomainDnsRecord> records,
        string purpose
    ) =>
        records
            .Where(record => record.Purpose == purpose && record.State != "deprecated")
            .OrderBy(record => record.State == "pending" ? 0 : 1)
            .FirstOrDefault();

    /// <summary>
    /// Every current record for a purpose that has several, such as <see cref="InboundMx"/>,
    /// in a stable order (by value, then host) so each keeps the same DNS resource.
    /// </summary>
    public static IReadOnlyList<SendingDomainDnsRecord> SelectAll(
        IEnumerable<SendingDomainDnsRecord> records,
        string purpose
    ) =>
        records
            .Where(record => record.Purpose == purpose && record.State != "deprecated")
            .OrderBy(record => record.Value, StringComparer.Ordinal)
            .ThenBy(record => record.Host, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Like <see cref="Select"/>, but fails with a descriptive error when Bird reports no such record.
    /// </summary>
    public static SendingDomainDnsRecord Require(
        IEnumerable<SendingDomainDnsRecord> records,
        string purpose,
        string domainName
    ) =>
        Select(records, purpose)
        ?? throw new InvalidOperationException(
            $"Bird did not report a {purpose} DNS record for {domainName}."
        );
}
