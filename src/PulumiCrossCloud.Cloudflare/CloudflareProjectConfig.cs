using Pulumi;

namespace PulumiCrossCloud.Cloudflare;

public static class CloudflareProjectConfig
{
    private static string? _accountId;
    private static string? _zoneId;

    public static string AccountId
    {
        get
        {
            if (!string.IsNullOrEmpty(_accountId))
                return _accountId;
            var config = new Config("cloudflare");
            return config.Get("accountId") ?? string.Empty;
        }
        set => _accountId = value;
    }

    public static string ZoneId
    {
        get
        {
            if (!string.IsNullOrEmpty(_zoneId))
                return _zoneId;
            var config = new Config("cloudflare");
            return config.Get("zoneId") ?? string.Empty;
        }
        set => _zoneId = value;
    }

    public static void Reset()
    {
        _accountId = null;
        _zoneId = null;
    }
}
