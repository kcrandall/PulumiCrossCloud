using Pulumi;

namespace PulumiCrossCloud.Gcp;

public static class GcpProjectConfig
{
    // Internal variables that can be set programmatically
    private static string? _projectId;
    private static string? _region;
    private static string? _zone;

    /// <summary>
    /// Gets or sets the GCP project ID.
    /// When getting: returns programmatic value, Pulumi config, environment variable, or throws exception.
    /// </summary>
    public static string ProjectId
    {
        get
        {
            // Check programmatically set value first
            if (!string.IsNullOrEmpty(_projectId))
            {
                return _projectId;
            }

            // Fallback to Pulumi config
            var config = new Config("gcp");
            var configProject = config.Get("project");
            if (!string.IsNullOrEmpty(configProject))
            {
                return configProject;
            }

            // Fallback to environment variable
            var envProject = System.Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");
            if (!string.IsNullOrEmpty(envProject))
            {
                return envProject;
            }

            throw new InvalidOperationException(
                "GCP Project ID not configured. Set via:\n"
                    + "1. GcpProjectConfig.ProjectId = \"your-project-id\"\n"
                    + "2. pulumi config set gcp:project YOUR_PROJECT_ID\n"
                    + "3. Environment variable GOOGLE_CLOUD_PROJECT"
            );
        }
        set => _projectId = value;
    }

    /// <summary>
    /// Gets or sets the GCP region.
    /// When getting: returns programmatic value, Pulumi config, or default us-central1.
    /// </summary>
    public static string Region
    {
        get
        {
            // Check programmatically set value first
            if (!string.IsNullOrEmpty(_region))
            {
                return _region;
            }

            // Fallback to Pulumi config
            var config = new Config("gcp");
            var configRegion = config.Get("region");
            if (!string.IsNullOrEmpty(configRegion))
            {
                return configRegion;
            }

            // Default fallback
            return "us-central1";
        }
        set => _region = value;
    }

    /// <summary>
    /// Gets or sets the GCP zone.
    /// When getting: returns programmatic value, Pulumi config, or default us-central1-a.
    /// </summary>
    public static string Zone
    {
        get
        {
            // Check programmatically set value first
            if (!string.IsNullOrEmpty(_zone))
            {
                return _zone;
            }

            // Fallback to Pulumi config
            var config = new Config("gcp");
            var configZone = config.Get("zone");
            if (!string.IsNullOrEmpty(configZone))
            {
                return configZone;
            }

            // Default fallback
            return "us-central1-a";
        }
        set => _zone = value;
    }

    /// <summary>
    /// Clears all programmatically set values, forcing fallback to config
    /// </summary>
    public static void Reset()
    {
        _projectId = null;
        _region = null;
        _zone = null;
    }

    /// <summary>
    /// Gets the current configuration as a summary string
    /// </summary>
    public static string ConfigSummary =>
        $"GCP Configuration:\n"
        + $"  Project: {ProjectId} {(string.IsNullOrEmpty(_projectId) ? "(from config/env)" : "(programmatic)")}\n"
        + $"  Region:  {Region} {(string.IsNullOrEmpty(_region) ? "(from config/default)" : "(programmatic)")}\n"
        + $"  Zone:    {Zone} {(string.IsNullOrEmpty(_zone) ? "(from config/default)" : "(programmatic)")}";
}
