using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PulumiCrossCloud.Bird.Provider;

/// <summary>
/// Minimal client for Bird's sending-domain API (<c>/v1/email/domains</c>).
/// Retries rate-limited and transient server failures, reusing one idempotency key per call.
/// </summary>
internal sealed class BirdApiClient
{
    private const int MaxAttempts = 5;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly Uri _baseUri;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public BirdApiClient(
        HttpClient http,
        string apiKey,
        Uri baseUri,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        _http = http;
        _apiKey = apiKey;
        _baseUri = baseUri;
        _delay = delay ?? Task.Delay;
    }

    public Task<BirdDomain> CreateDomainAsync(JsonObject body, CancellationToken ct) =>
        SendRequiredAsync(HttpMethod.Post, "v1/email/domains", body, ct);

    public async Task<BirdDomain?> GetDomainAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, DomainPath(id), null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        return await ReadDomainAsync(response, HttpMethod.Get, DomainPath(id), ct);
    }

    public Task<BirdDomain> UpdateDomainAsync(string id, JsonObject body, CancellationToken ct) =>
        SendRequiredAsync(HttpMethod.Patch, DomainPath(id), body, ct);

    public Task<BirdDomain> VerifyDomainAsync(string id, CancellationToken ct) =>
        SendRequiredAsync(HttpMethod.Post, $"{DomainPath(id)}/verify", null, ct);

    /// <summary>
    /// Deletes the domain. Returns false when it no longer exists.
    /// </summary>
    public async Task<bool> DeleteDomainAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, DomainPath(id), null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        await EnsureSuccessAsync(response, HttpMethod.Delete, DomainPath(id), ct);
        return true;
    }

    private static string DomainPath(string id) => $"v1/email/domains/{Uri.EscapeDataString(id)}";

    private async Task<BirdDomain> SendRequiredAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        CancellationToken ct
    )
    {
        using var response = await SendAsync(method, path, body, ct);
        return await ReadDomainAsync(response, method, path, ct);
    }

    private static async Task<BirdDomain> ReadDomainAsync(
        HttpResponseMessage response,
        HttpMethod method,
        string path,
        CancellationToken ct
    )
    {
        await EnsureSuccessAsync(response, method, path, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<BirdDomain>(stream, JsonOptions, ct)
            ?? throw new BirdApiException(
                response.StatusCode,
                $"Bird API {method} /{path} returned an empty body."
            );
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        CancellationToken ct
    )
    {
        var idempotencyKey = method == HttpMethod.Get ? null : $"pulumi/{Guid.NewGuid():N}";
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (idempotencyKey is not null)
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            if (body is not null)
            {
                // A sized body rather than a chunked stream, which some proxies reject.
                request.Content = new ByteArrayContent(
                    JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions)
                );
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                );
            }
            catch (HttpRequestException error)
            {
                throw new HttpRequestException(
                    $"Bird API {method} /{path} could not be reached: {error.InnerException?.Message ?? error.Message}",
                    error
                );
            }
            if (attempt >= MaxAttempts || !IsRetryable(response.StatusCode))
                return response;

            var wait = RetryDelay(response, attempt);
            response.Dispose();
            await _delay(wait, ct);
        }
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status
            is HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return Clamp(delta);
        if (retryAfter?.Date is { } date)
            return Clamp(date - DateTimeOffset.UtcNow);

        return TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

        static TimeSpan Clamp(TimeSpan value) =>
            value < TimeSpan.Zero ? TimeSpan.Zero
            : value > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60)
            : value;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        HttpMethod method,
        string path,
        CancellationToken ct
    )
    {
        if (response.IsSuccessStatusCode)
            return;

        BirdError? error = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            error = (
                await JsonSerializer.DeserializeAsync<BirdErrorEnvelope>(stream, JsonOptions, ct)
            )?.Error;
        }
        catch (JsonException)
        {
            // Non-JSON error bodies (proxies, load balancers) fall back to the status code.
        }

        var detail = error?.Message ?? response.ReasonPhrase ?? "request failed";
        var code = error?.Code is null ? "" : $" {error.Code}";
        var requestId = error?.RequestId is null ? "" : $" (request {error.RequestId})";
        throw new BirdApiException(
            response.StatusCode,
            $"Bird API {method} /{path} failed with {(int)response.StatusCode}{code}: {detail}{requestId}",
            error?.Code
        );
    }
}

internal sealed class BirdApiException(
    HttpStatusCode statusCode,
    string message,
    string? code = null
) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}
