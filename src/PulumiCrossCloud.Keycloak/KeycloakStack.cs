using System.Reflection;
using System.Runtime.CompilerServices;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Keycloak;

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

    public KeycloakStack(
        CloudResourceFactory factory,
        string name,
        ISecret postgresUrlSecret,
        ISecret postgresUserSecret,
        ISecret postgresPasswordSecret,
        PulumiRealmConfig? realmConfig = null
    )
        : base("keycloak:Stack", name, null, null)
    {
        this.postgresUrlSecret = postgresUrlSecret;
        this.postgresUserSecret = postgresUserSecret;
        this.postgresPasswordSecret = postgresPasswordSecret;

        // Write embedded Dockerfile to a temp file
        var dockerfilePath = WriteEmbeddedDockerfileToTemp();
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

        // Add required secrets/vars
        compute.AddEnvironmentVariable("KEYCLOAK_ADMIN", "admin");
        compute.AddRequiredSecret(keycloakAdminPasswordSecret, "KEYCLOAK_ADMIN_PASSWORD");
        compute.AddEnvironmentVariable("KC_DB", "postgres");
        compute.AddRequiredSecret(postgresUrlSecret, "KC_DB_URL");
        compute.AddRequiredSecret(postgresUserSecret, "KC_DB_USERNAME");
        compute.AddRequiredSecret(postgresPasswordSecret, "KC_DB_PASSWORD");

        // Configure health check using cross-cloud abstraction
        compute.HealthCheckConfiguration = new HealthCheckConfiguration
        {
            Path = "/health/ready", // Keycloak's readiness endpoint
            IntervalSeconds = 10, // More reasonable interval for DB-backed app
            TimeoutSeconds = 5, // Longer timeout for health checks
            UnhealthyThreshold = 3,
            HealthyThreshold = 2,
            StartupTimeoutSeconds = 10, // Timeout per individual startup probe request
            StartupIntervalSeconds = 15, // Check every 15 seconds during startup
            StartupUnhealthyThreshold = 20 // 20 failures × 15 seconds = 300 seconds (5 minutes total)
        };

        // Configure port forwarding for Keycloak
        compute.PortForwardings = [new PortForwardingConfiguration { ContainerPort = 8080 }];

        // Set scaling options
        compute.MinCount = 0;
        compute.MaxCount = 1;

        image.PushImage(dockerfilePath, contextPath);
        compute.Build();

        // After compute is ready, configure Keycloak realm/clients/idps using the native Pulumi.Keycloak provider
        if (realmConfig is not null)
        {
            // Use Output.All to combine ServiceUrl and admin password, then configure Keycloak
            RealmResults = Output
                .Tuple(
                    compute.ServiceUrl,
                    keycloakAdminPasswordSecret.Value.Apply(s => s ?? string.Empty)
                )
                .Apply(t =>
                {
                    var (serviceUrl, adminPassword) = t;

                    if (string.IsNullOrWhiteSpace(serviceUrl))
                        throw new InvalidOperationException("Keycloak ServiceUrl is empty.");
                    if (string.IsNullOrWhiteSpace(adminPassword))
                        throw new InvalidOperationException("Admin password secret is empty.");

                    // Wait for KC to be ready
                    var readyScript =
                        $"bash -lc 'for i in {{1..60}}; do if curl -sf {serviceUrl}/health/ready >/dev/null; then echo ready; exit 0; fi; sleep 5; done; echo timeout; exit 1'";
                    var readyCheck = new Pulumi.Command.Local.Command(
                        $"wait-for-keycloak-{name}",
                        new Pulumi.Command.Local.CommandArgs
                        {
                            Create = readyScript,
                            Update = readyScript
                        }
                    );

                    var providerArgs =
                        realmConfig!.ProviderOverrides
                        ?? new Pulumi.Keycloak.ProviderArgs
                        {
                            Url = serviceUrl.TrimEnd('/'),
                            ClientId = "admin-cli",
                            Username = "admin",
                            Password = adminPassword,
                            ClientTimeout = 120
                        };

                    var provider = new Pulumi.Keycloak.Provider(
                        $"{name}-kc-provider",
                        providerArgs,
                        new CustomResourceOptions { DependsOn = new[] { readyCheck } } // ensure readiness
                    );

                    var result = PulumiRealmDeployer.Deploy(
                        realmConfig,
                        provider,
                        factory,
                        serviceUrl,
                        dependsOn: new Resource[] { readyCheck }
                    );

                    // Return the realm deployment result
                    return Output.Create(result);
                });
        }
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
        {
            chars[i] = allowedChars[bytes[i] % allowedChars.Length];
        }
        return new string(chars);
    }

    /// <summary>
    /// Writes the embedded Dockerfile to a temporary file and returns its path.
    /// </summary>
    private static string WriteEmbeddedDockerfileToTemp([CallerFilePath] string sourceFilePath = "")
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
            Path.GetTempPath(),
            ".pulumi-docker",
            $"keycloak-build-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(tempDir);
        var dockerfilePath = Path.Combine(tempDir, "Dockerfile");
        File.WriteAllText(dockerfilePath, dockerfileContent);
        Console.WriteLine($"[KeycloakStack] Dockerfile written to temp path: {dockerfilePath}");
        return dockerfilePath;
    }
}
