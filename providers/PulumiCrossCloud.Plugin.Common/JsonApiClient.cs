using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace PulumiCrossCloud.Plugin.Common;

/// <summary>
/// Shared HTTP plumbing for vendor JSON APIs: authentication, sized JSON bodies, retries for
/// rate limits and transient server errors (honoring Retry-After), and descriptive errors.
/// </summary>
public abstract class JsonApiClient
{
    private const int MaxAttempts = 5;

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly AuthenticationHeaderValue _authorization;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly bool _sendIdempotencyKeys;

    protected JsonApiClient(
        string service,
        HttpClient http,
        Uri baseUri,
        AuthenticationHeaderValue authorization,
        Func<TimeSpan, CancellationToken, Task>? delay,
        bool sendIdempotencyKeys = false
    )
    {
        Service = service;
        _http = http;
        _baseUri = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri($"{baseUri.AbsoluteUri}/");
        _authorization = authorization;
        _delay = delay ?? Task.Delay;
        _sendIdempotencyKeys = sendIdempotencyKeys;
    }

    protected string Service { get; }

    /// <summary>
    /// Sends a request and returns the deserialized body, or null for 404 when <paramref name="allowNotFound"/> is set.
    /// </summary>
    protected async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        JsonNode? body,
        JsonTypeInfo<T> type,
        CancellationToken ct,
        bool allowNotFound = false
    )
        where T : class
    {
        using var response = await SendAsync(method, path, body, ct);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, method, path, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, type, ct)
            ?? throw new ApiException(
                Service,
                response.StatusCode,
                $"{Service} API {method} /{path} returned an empty body."
            );
    }

    /// <summary>
    /// Sends a request without reading a body. Returns false for 404 when <paramref name="allowNotFound"/> is set.
    /// </summary>
    protected async Task<bool> SendAsync(
        HttpMethod method,
        string path,
        CancellationToken ct,
        bool allowNotFound = false
    )
    {
        using var response = await SendAsync(method, path, null, ct);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            return false;

        await EnsureSuccessAsync(response, method, path, ct);
        return true;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        CancellationToken ct
    )
    {
        var idempotencyKey =
            _sendIdempotencyKeys && method != HttpMethod.Get ? $"pulumi/{Guid.NewGuid():N}" : null;
        var payload = body is null ? null : Encoding.UTF8.GetBytes(body.ToJsonString());
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
            request.Headers.Authorization = _authorization;
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (idempotencyKey is not null)
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            if (payload is not null)
            {
                // A sized body rather than a chunked stream, which some servers and proxies reject.
                request.Content = new ByteArrayContent(payload);
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
            catch (HttpRequestException)
                when (attempt < MaxAttempts && CanRepeat(method, idempotencyKey))
            {
                // Connection resets and similar transport failures are retried when repeating
                // the request cannot duplicate work.
                await _delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
                continue;
            }
            catch (HttpRequestException error)
            {
                throw new HttpRequestException(
                    $"{Service} API {method} /{path} could not be reached: {error.InnerException?.Message ?? error.Message}",
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

    /// <summary>
    /// Whether a request whose outcome is unknown can be sent again safely: every method except
    /// POST, and POST when the API deduplicates it by idempotency key.
    /// </summary>
    private static bool CanRepeat(HttpMethod method, string? idempotencyKey) =>
        method != HttpMethod.Post || idempotencyKey is not null;

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

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        HttpMethod method,
        string path,
        CancellationToken ct
    )
    {
        if (response.IsSuccessStatusCode)
            return;

        string? code = null;
        string? detail = null;
        string? requestId = response.Headers.TryGetValues("X-Request-Id", out var ids)
            ? ids.FirstOrDefault()
            : null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            // Bird: {"error":{"code","message","request_id"}}; PostHog: {"code","detail"}; Mailgun: {"message"}.
            var root = document.RootElement;
            var error =
                root.TryGetProperty("error", out var nested)
                && nested.ValueKind == JsonValueKind.Object
                    ? nested
                    : root;
            code = StringProperty(error, "code");
            detail = StringProperty(error, "message") ?? StringProperty(error, "detail");
            requestId = StringProperty(error, "request_id") ?? requestId;
        }
        catch (JsonException)
        {
            // Non-JSON error bodies (proxies, load balancers) fall back to the status code.
        }

        detail ??= response.ReasonPhrase ?? "request failed";
        var codeText = code is null ? "" : $" {code}";
        var requestText = requestId is null ? "" : $" (request {requestId})";
        throw new ApiException(
            Service,
            response.StatusCode,
            $"{Service} API {method} /{path} failed with {(int)response.StatusCode}{codeText}: {detail}{requestText}",
            code
        );
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

public sealed class ApiException(
    string service,
    HttpStatusCode statusCode,
    string message,
    string? code = null
) : Exception(message)
{
    public string Service { get; } = service;

    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}
