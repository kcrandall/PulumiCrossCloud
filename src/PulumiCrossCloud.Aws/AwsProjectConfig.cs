using Pulumi;

namespace PulumiCrossCloud.Aws;

public static class AwsProjectConfig
{
    private static string? _region;

    /// <summary>
    /// Gets or sets the AWS region.
    /// When getting: returns programmatic value, Pulumi config, or default us-east-1.
    /// </summary>
    public static string Region
    {
        get
        {
            if (!string.IsNullOrEmpty(_region))
                return _region;

            var config = new Config("aws");
            var configRegion = config.Get("region");
            if (!string.IsNullOrEmpty(configRegion))
                return configRegion;

            return "us-east-1";
        }
        set => _region = value;
    }

    public static void Reset()
    {
        _region = null;
    }
}
