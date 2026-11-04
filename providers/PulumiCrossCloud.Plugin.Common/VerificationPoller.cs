namespace PulumiCrossCloud.Plugin.Common;

/// <summary>
/// Repeats a vendor's DNS verification check with backoff until it reports ready, fails
/// permanently, or the timeout elapses. Time is measured by the delays awaited, so tests can
/// substitute an instant delay.
/// </summary>
public sealed class VerificationPoller(Func<TimeSpan, CancellationToken, Task> delay)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan InitialInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(30);

    /// <param name="check">Runs one verification check.</param>
    /// <param name="ready">Whether the result is ready to send.</param>
    /// <param name="permanentFailure">An error message when the result can never become ready, otherwise null.</param>
    /// <param name="describePending">Explains what is still pending when the timeout elapses.</param>
    /// <param name="wait">When false, run a single check and return it.</param>
    /// <param name="timeout">The wait budget; zero or negative uses <see cref="DefaultTimeout"/>.</param>
    public async Task<T> WaitAsync<T>(
        Func<CancellationToken, Task<T>> check,
        Func<T, bool> ready,
        Func<T, string?> permanentFailure,
        Func<T, TimeSpan, string> describePending,
        bool wait,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        var budget = timeout > TimeSpan.Zero ? timeout : DefaultTimeout;
        var waited = TimeSpan.Zero;
        var interval = InitialInterval;
        while (true)
        {
            var result = await check(ct);
            if (!wait || ready(result))
                return result;
            if (permanentFailure(result) is { } failure)
                throw new InvalidOperationException(failure);
            if (waited + interval > budget)
                throw new TimeoutException(describePending(result, budget));

            await delay(interval, ct);
            waited += interval;
            interval = TimeSpan.FromTicks(
                Math.Min((long)(interval.Ticks * 1.5), MaxInterval.Ticks)
            );
        }
    }
}
