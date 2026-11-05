using System.Reflection;
using System.Runtime.CompilerServices;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Keycloak;

/// <summary>
/// Configuration for Keycloak theme providers (JAR files).
/// </summary>
public sealed class KeycloakThemeProviders
{
    /// <summary>
    /// List of theme JAR file paths to copy to the Keycloak providers directory.
    /// These files should be Keycloakify-generated JAR files or other Keycloak theme providers.
    /// </summary>
    public List<string> ThemeJarFiles { get; init; } = [];

    /// <summary>
    /// Optional custom themes directory to copy to /opt/keycloak/themes.
    /// This is an alternative to JAR-based themes.
    /// </summary>
    public string? CustomThemesDirectory { get; init; }
}

/// <summary>
/// KeycloakStack provisions a Keycloak instance using the cross-cloud abstractions.
///
/// Environment Variables Injected:
///   KC_DB=postgres
///   KC_DB_URL=jdbc:postgresql://${pg_host}:${pg_port}/${pg_db}
///   KC_DB_USERNAME=... (from secret)
///   KC_DB_PASSWORD=... (from secret)
///   KEYCLOAK_ADMIN=admin
///   KEYCLOAK_ADMIN_PASSWORD=... (randomly generated)
///
/// Usage Example:
/// <code>
/// var stack = new KeycloakStack(
///     factory,
///     "my-keycloak",
///     postgresUrlSecret,
///     postgresUserSecret,
///     postgresPasswordSecret
/// );
/// </code>
///
/// KC_DB_URL format:
///   export KC_DB_URL="jdbc:postgresql://${pg_host}:${pg_port}/${pg_db}"
/// </summary>
public class KeycloakStack : ComponentResource
{
    private ICompute compute;
    private ISecret keycloakAdminPasswordSecret;
    private ISecret postgresUrlSecret;
    private ISecret postgresUserSecret;
    private ISecret postgresPasswordSecret;

    /// <summary>
    /// Gets the realm deployment results including created resources and client configuration results.
    /// </summary>
    public Output<RealmDeploymentResult> RealmResults { get; private set; } = null!;

    /// <summary>
    /// Gets the Keycloak service URL directly from the compute resource.
    /// This is available immediately after the compute resource is deployed.
    /// </summary>
    public Output<string> KeycloakUrl => compute.ServiceUrl;

    public KeycloakStack(
        CloudResourceFactory factory,
        string name,
        ISecret postgresUrlSecret,
        ISecret postgresUserSecret,
        ISecret postgresPasswordSecret,
        PulumiRealmConfig? realmConfig = null,
        KeycloakThemeProviders? themeProviders = null
    )
        : base("keycloak:Stack", name, null, null)
    {
        this.postgresUrlSecret = postgresUrlSecret;
        this.postgresUserSecret = postgresUserSecret;
        this.postgresPasswordSecret = postgresPasswordSecret;

        // Write embedded Dockerfile to a temp file
        var dockerfilePath = WriteEmbeddedDockerfileToTemp(themeProviders);
        var contextPath = Path.GetDirectoryName(dockerfilePath)!;

        // Create container registry for Keycloak (import default hello-world image)
        var registry = factory.CreateContainerRegistry(
            $"{name}-registry",
            createDefaultImage: false
        );

        var image = factory.CreateContainerImage("keycloak", registry);

        // Generate a secure random password for Keycloak admin
        string keycloakAdminPassword = GenerateSecurePassword(24);
        keycloakAdminPasswordSecret = factory.CreateSecret(
            $"{name}-admin-password",
            keycloakAdminPassword,
            ignoreChangesAfterCreate: true
        );

        // Create compute resource for Keycloak
        compute = factory.CreateCompute(
            name: name ?? "keycloak",
            registry: registry,
            imageTag: "latest",
            // imageName: "keycloak",
            containerImage: image,
            serviceType: null // Uses default per cloud
        );

        // Configure resource limits for Keycloak (memory-intensive application)
        compute.MemoryLimit = "2Gi"; // 2GB memory for Keycloak with database
        compute.CpuLimit = "2"; // 2 vCPUs for better startup performance

        // Add required secrets/vars (database connection is still needed at runtime)
        compute.AddEnvironmentVariable("KEYCLOAK_ADMIN", "admin");
        compute.AddRequiredSecret(keycloakAdminPasswordSecret, "KEYCLOAK_ADMIN_PASSWORD");
        // KC_DB is baked into the image, but connection details are still runtime secrets
        compute.AddRequiredSecret(postgresUrlSecret, "KC_DB_URL");
        compute.AddRequiredSecret(postgresUserSecret, "KC_DB_USERNAME");
        compute.AddRequiredSecret(postgresPasswordSecret, "KC_DB_PASSWORD");

        // Runtime logging configuration (can be different from build-time defaults)
        compute.AddEnvironmentVariable("KC_LOG_LEVEL", "INFO");
        compute.AddEnvironmentVariable("KC_LOG_CONSOLE_OUTPUT", "default"); // default,json
        compute.AddEnvironmentVariable("KC_LOG_CONSOLE_COLOR", "false");

        // Set all Keycloak configuration variables at runtime to ensure they're available
        // These mirror the build-time configuration but may be needed at runtime too
        var keycloakConfig = GetKeycloakConfiguration();
        foreach (var (key, value) in keycloakConfig)
            compute.AddEnvironmentVariable(key, value);

        // Configure health check using cross-cloud abstraction
        compute.HealthCheckConfiguration = new HealthCheckConfiguration
        {
            Path = "/health/ready",
            IntervalSeconds = 10, // More reasonable interval for DB-backed app
            TimeoutSeconds = 5, // Longer timeout for health checks
            UnhealthyThreshold = 3,
            HealthyThreshold = 2,
            StartupTimeoutSeconds = 10, // Timeout per individual startup probe request
            StartupIntervalSeconds = 15, // Check every 15 seconds during startup
            StartupUnhealthyThreshold = 20, // 20 failures × 15 seconds = 300 seconds (5 minutes total)
        };

        // Configure port forwarding for Keycloak
        compute.PortForwardings = [new PortForwardingConfiguration { ContainerPort = 8080 }];

        // Set scaling options
        compute.MinCount = 0;
        compute.MaxCount = 1;

        image.PushImage(dockerfilePath, contextPath);
        compute.Build();

        // Get dependencies after build is complete
        var computeDependencies = compute.GetDependencies().ToArray();

        // After compute is ready, configure Keycloak realm/clients/idps using the native Pulumi.Keycloak provider
        if (realmConfig is not null)
        {
            // Wait for /health/ready once the compute and its dependencies exist. Previews skip it.
            var readyServiceUrl = Output
                .Tuple(
                    compute.ServiceUrl,
                    Output.All(
                        computeDependencies.Select(resource =>
                            resource is CustomResource custom ? custom.Id : resource.Urn
                        )
                    )
                )
                .Apply(async t =>
                {
                    var serviceUrl = t.Item1;
                    if (!Pulumi.Deployment.Instance.IsDryRun && !string.IsNullOrWhiteSpace(serviceUrl))
                        await KeycloakReadiness.WaitAsync(serviceUrl);
                    return serviceUrl;
                });

            // Use Output.All to combine ServiceUrl and admin password, then configure Keycloak
            RealmResults = Output
                .Tuple(
                    readyServiceUrl,
                    keycloakAdminPasswordSecret.Value.Apply(s => s ?? string.Empty)
                )
                .Apply(t =>
                {
                    var (serviceUrl, adminPassword) = t;

                    // During preview/dry run, URLs might be empty - handle gracefully
                    if (string.IsNullOrWhiteSpace(serviceUrl))
                    {
                        if (Pulumi.Deployment.Instance.IsDryRun)
                        {
                            Log.Info(
                                "[PREVIEW] Keycloak ServiceUrl not available during preview - will be configured during actual deployment"
                            );
                            // Skip realm configuration during preview
                            return Output.Create(
                                new RealmDeploymentResult
                                {
                                    Realm = null!, // Will be created during actual deployment
                                    CreatedResources = new List<Resource>(),
                                    ClientResults =
                                        new Dictionary<string, Output<KeycloakClientResult>>(),
                                }
                            );
                        }
                        else
                            throw new InvalidOperationException(
                                "Keycloak ServiceUrl is empty during actual deployment."
                            );
                    }

                    if (string.IsNullOrWhiteSpace(adminPassword))
                    {
                        if (Pulumi.Deployment.Instance.IsDryRun)
                        {
                            Log.Info(
                                "[PREVIEW] Keycloak admin password not available during preview - will be configured during actual deployment"
                            );
                            // Skip realm configuration during preview
                            return Output.Create(
                                new RealmDeploymentResult
                                {
                                    Realm = null!, // Will be created during actual deployment
                                    CreatedResources = new List<Resource>(),
                                    ClientResults =
                                        new Dictionary<string, Output<KeycloakClientResult>>(),
                                }
                            );
                        }
                        else
                            throw new InvalidOperationException(
                                "Admin password secret is empty during actual deployment."
                            );
                    }

                    var providerArgs =
                        realmConfig!.ProviderOverrides
                        ?? new Pulumi.Keycloak.ProviderArgs
                        {
                            Url = serviceUrl.TrimEnd('/'),
                            ClientId = "admin-cli",
                            Username = "admin",
                            Password = adminPassword,
                            ClientTimeout = 120,
                        };

                    var provider = new Pulumi.Keycloak.Provider(
                        $"{name}-kc-provider",
                        providerArgs,
                        new CustomResourceOptions { DependsOn = computeDependencies }
                    );

                    var result = PulumiRealmDeployer.Deploy(
                        realmConfig,
                        provider,
                        factory,
                        serviceUrl,
                        dependsOn: computeDependencies
                    );

                    // Return the realm deployment result
                    return Output.Create(result);
                });
        }
    }

    /// <summary>
    /// Gets the shared Keycloak configuration values used for both build-time and runtime.
    /// These values are consistent across the Docker build process and container runtime.
    /// </summary>
    private static Dictionary<string, string> GetKeycloakConfiguration()
    {
        return new Dictionary<string, string>
        {
            ["KC_DB"] = "postgres",
            ["KC_HEALTH_ENABLED"] = "true",
            ["KC_METRICS_ENABLED"] = "true",
            ["KC_FEATURES"] = "token-exchange",
            ["KC_HTTP_ENABLED"] = "true",
            ["KC_HOSTNAME_STRICT"] = "false",
            ["KC_PROXY"] = "edge",
            ["KC_CACHE"] = "local",
            ["KC_PROXY_HEADERS"] = "xforwarded",
            ["KC_HTTP_PORT"] = "8080",
            ["KC_LEGACY_OBSERVABILITY_INTERFACE"] = "true",
        };
    }

    /// <summary>
    /// Generates a secure random password of the specified length using .NET cryptography.
    /// </summary>
    private static string GenerateSecurePassword(int length)
    {
        const string allowedChars =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-_=+";
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = allowedChars[bytes[i] % allowedChars.Length];

        return new string(chars);
    }

    /// <summary>
    /// Writes the embedded Dockerfile to a temporary file and returns its path.
    /// Optionally copies theme provider files to the build context.
    /// </summary>
    private static string WriteEmbeddedDockerfileToTemp(
        KeycloakThemeProviders? themeProviders = null,
        [CallerFilePath] string sourceFilePath = ""
    )
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();
        var resourceName = resourceNames.FirstOrDefault(n => n.EndsWith("Dockerfile"));
        if (resourceName is null)
            throw new InvalidOperationException("Dockerfile not embedded as resource.");
        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream!);
        var dockerfileContent = reader.ReadToEnd();

        // Create an isolated temp directory to avoid hashing issues with system temp sockets
        var tempDir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            ".pulumi-docker",
            $"keycloak"
        );
        Directory.CreateDirectory(tempDir);

        // Always modify the Dockerfile to hardcode build-time configuration
        var dockerfileBuilder = new System.Text.StringBuilder(dockerfileContent);

        // Use shared Keycloak configuration for consistency between build and runtime
        var keycloakConfig = GetKeycloakConfiguration();

        // Replace placeholder variables in the Dockerfile with actual values
        foreach (var (key, value) in keycloakConfig)
            dockerfileBuilder.Replace($"${{{key}}}", value);

        // Handle theme providers if provided - use placeholder replacement
        var themeCopyCommands = new System.Text.StringBuilder();

        if (themeProviders is not null)
        {
            // Add theme JAR files to providers directory
            if (themeProviders.ThemeJarFiles.Count > 0)
            {
                // Create providers directory in build context
                var providersDir = Path.Combine(tempDir, "providers");
                Directory.CreateDirectory(providersDir);

                themeCopyCommands.AppendLine("# Copy theme provider JAR files");

                foreach (var jarFile in themeProviders.ThemeJarFiles)
                    if (File.Exists(jarFile))
                    {
                        var jarFileName = Path.GetFileName(jarFile);
                        var destPath = Path.Combine(providersDir, jarFileName);
                        File.Copy(jarFile, destPath, overwrite: true);
                        themeCopyCommands.AppendLine(
                            $"COPY providers/{jarFileName} /opt/keycloak/providers/"
                        );
                        Console.WriteLine(
                            $"[KeycloakStack] Copied theme JAR: {jarFile} -> providers/{jarFileName}"
                        );
                    }
                    else
                        Console.WriteLine(
                            $"[KeycloakStack] Warning: Theme JAR file not found: {jarFile}"
                        );
            }

            // Add custom themes directory if provided
            if (
                !string.IsNullOrWhiteSpace(themeProviders.CustomThemesDirectory)
                && Directory.Exists(themeProviders.CustomThemesDirectory)
            )
            {
                var themesDir = Path.Combine(tempDir, "themes");
                CopyDirectory(themeProviders.CustomThemesDirectory, themesDir);
                if (themeCopyCommands.Length > 0)
                    themeCopyCommands.AppendLine();
                themeCopyCommands.AppendLine("# Copy custom themes");
                themeCopyCommands.AppendLine("COPY themes/ /opt/keycloak/themes/");
                Console.WriteLine(
                    $"[KeycloakStack] Copied custom themes: {themeProviders.CustomThemesDirectory} -> themes/"
                );
            }
        }

        // Replace theme placeholder with actual COPY commands
        var themeContent =
            themeCopyCommands.Length > 0
                ? themeCopyCommands.ToString().TrimEnd()
                : "# No custom themes configured";
        dockerfileBuilder.Replace("# {THEMES}", themeContent);

        // Use the modified dockerfile with hardcoded values and theme providers (if any)
        dockerfileContent = dockerfileBuilder.ToString();

        var dockerfilePath = Path.Combine(tempDir, "Dockerfile");
        File.WriteAllText(dockerfilePath, dockerfileContent);
        Console.WriteLine($"[KeycloakStack] Dockerfile written to temp path: {dockerfilePath}");
        return dockerfilePath;
    }

    /// <summary>
    /// Recursively copies a directory and all its contents.
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        // Copy files
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        // Copy subdirectories
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }
}
