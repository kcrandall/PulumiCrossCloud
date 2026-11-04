namespace PulumiCrossCloud.Bird.Provider;

/// <summary>
/// Bird hosts each organization in one region. API keys carry it as a prefix
/// (<c>bk_us1_…</c> workspace keys, <c>bm_eu1_…</c> organization master keys).
/// </summary>
internal static class BirdRegions
{
    public const string Default = "us1";

    public static readonly IReadOnlyList<string> Supported = ["us1", "eu1"];

    public static bool IsSupported(string region) => Supported.Contains(region);

    /// <summary>
    /// Returns the region encoded in a Bird API key, or null when the key has no recognized prefix.
    /// </summary>
    public static string? FromApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return null;

        var parts = apiKey.Split('_', 3);
        if (parts.Length < 3 || parts[0] is not ("bk" or "bm"))
            return null;

        return IsSupported(parts[1]) ? parts[1] : null;
    }

    public static Uri ApiBaseUri(string region) => new($"https://{region}.platform.bird.com/");
}
