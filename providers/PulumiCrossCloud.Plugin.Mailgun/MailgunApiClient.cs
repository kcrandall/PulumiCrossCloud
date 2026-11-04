using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PulumiCrossCloud.Plugin.Common;

namespace PulumiCrossCloud.Plugin.Mailgun;

/// <summary>
/// Client for Mailgun's v4 domain API, authenticated with <c>api:&lt;key&gt;</c> basic auth.
/// </summary>
internal sealed class MailgunApiClient(
    HttpClient http,
    string apiKey,
    Uri baseUri,
    Func<TimeSpan, CancellationToken, Task>? delay = null
)
    : JsonApiClient(
        "Mailgun",
        http,
        baseUri,
        new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"api:{apiKey}"))
        ),
        delay
    )
{
    public static Uri BaseUri(string region) =>
        region == "eu" ? new("https://api.eu.mailgun.net/") : new("https://api.mailgun.net/");

    public Task<MailgunDomainResponse?> GetDomainAsync(string domain, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Get,
            DomainPath(domain),
            null,
            MailgunJson.Default.MailgunDomainResponse,
            ct,
            allowNotFound: true
        );

    /// <summary>
    /// Asks Mailgun to re-check the domain's DNS records.
    /// </summary>
    public async Task<MailgunDomainResponse> VerifyDomainAsync(
        string domain,
        CancellationToken ct
    ) =>
        (
            await SendAsync(
                HttpMethod.Put,
                $"{DomainPath(domain)}/verify",
                null,
                MailgunJson.Default.MailgunDomainResponse,
                ct
            )
        )!;

    private static string DomainPath(string domain) => $"v4/domains/{Uri.EscapeDataString(domain)}";
}

internal sealed record MailgunDomainResponse
{
    public MailgunDomain Domain { get; init; } = new();
    public List<MailgunDnsRecord> SendingDnsRecords { get; init; } = [];
}

internal sealed record MailgunDomain
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>active, unverified or disabled.</summary>
    public string State { get; init; } = "";
    public bool IsDisabled { get; init; }
}

internal sealed record MailgunDnsRecord
{
    public string RecordType { get; init; } = "";
    public string? Name { get; init; }
    public string Value { get; init; } = "";

    /// <summary>valid, invalid or unknown.</summary>
    public string Valid { get; init; } = "";
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower
)]
[JsonSerializable(typeof(MailgunDomainResponse))]
internal sealed partial class MailgunJson : JsonSerializerContext;
