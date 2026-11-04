namespace PulumiCrossCloud.Bird;

/// <summary>
/// The region a Bird organization is hosted in. It selects both the API and SMTP hosts.
/// </summary>
public enum BirdRegion
{
    Us1,
    Eu1,
}

public static class BirdRegionExtensions
{
    /// <summary>
    /// The region identifier Bird uses in hostnames and API key prefixes, such as <c>us1</c>.
    /// </summary>
    public static string ToApiValue(this BirdRegion region) =>
        region switch
        {
            BirdRegion.Us1 => "us1",
            BirdRegion.Eu1 => "eu1",
            _ => throw new ArgumentOutOfRangeException(nameof(region), region, null),
        };

    /// <summary>
    /// The SMTP relay host for the region, such as <c>us1.smtp.bird.com</c>.
    /// </summary>
    public static string SmtpHost(this BirdRegion region) => $"{region.ToApiValue()}.smtp.bird.com";
}
