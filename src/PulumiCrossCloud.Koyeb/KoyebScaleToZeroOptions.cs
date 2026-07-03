namespace PulumiCrossCloud.Koyeb;

/// <summary>
/// Provider-specific Koyeb Scale-to-Zero sleep settings.
/// Set <see cref="KoyebServiceCompute.MinCount" /> to 0 to enable Scale-to-Zero.
/// </summary>
public sealed class KoyebScaleToZeroOptions
{
    /// <summary>
    /// Delay after which an idle service enters Light Sleep.
    /// Use TimeSpan.Zero to disable Light Sleep.
    /// </summary>
    public TimeSpan? LightSleepDelay { get; set; }

    /// <summary>
    /// Delay after which an idle service enters Deep Sleep.
    /// Use TimeSpan.Zero to disable Deep Sleep.
    /// </summary>
    public TimeSpan? DeepSleepDelay { get; set; }

    public bool HasConfiguredSleepDelays => LightSleepDelay.HasValue || DeepSleepDelay.HasValue;
}
