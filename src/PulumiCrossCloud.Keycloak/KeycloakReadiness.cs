namespace PulumiCrossCloud.Keycloak;

/// <summary>
/// Waits for Keycloak's <c>/health/ready</c> endpoint before the realm is configured, from the
/// Pulumi program itself, so deployments need neither curl nor bash.
/// </summary>
internal static class KeycloakReadiness
{
    // One client for the program's lifetime.
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    public const int Attempts = 60;

    public static async Task WaitAsync(
        string serviceUrl,
        HttpClient? client = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken ct = default
    )
    {
        var http = client ?? SharedClient;
        var wait = delay ?? Task.Delay;
        var health = new Uri($"{serviceUrl.TrimEnd('/')}/health/ready");
        string? lastProblem = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(health, ct);
                if (response.IsSuccessStatusCode)
                    return;
                lastProblem = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // Starting containers refuse connections or time out; keep waiting.
                lastProblem = error.Message;
            }

            if (attempt < Attempts)
                await wait(Interval, ct);
        }

        throw new TimeoutException(
            $"Keycloak at {serviceUrl} was not ready after {Attempts * Interval.TotalSeconds / 60:0.#} minutes "
                + $"({health} last returned: {lastProblem})."
        );
    }
}
