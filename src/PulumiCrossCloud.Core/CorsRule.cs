namespace PulumiCrossCloud.Core;

/// <summary>
/// Generic CORS rule configuration that works across cloud providers.
/// </summary>
public record CorsRule(
    IEnumerable<string> AllowedOrigins,
    IEnumerable<string> AllowedMethods,
    IEnumerable<string> AllowedHeaders,
    IEnumerable<string> ExposedHeaders,
    int MaxAgeSeconds = 3600
);
