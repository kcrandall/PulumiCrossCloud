using Pulumi;
using PulumiCrossCloud.Core;
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
public class KeycloakStack : Stack
{
    public KeycloakStack(
        CloudResourceFactory factory,
        string name,
        ISecret postgresUrlSecret,
        ISecret postgresUserSecret,
        ISecret postgresPasswordSecret)
    {
        // Create container registry for Keycloak
        var registry = factory.CreateContainerRegistry($"{name}-keycloak-registry");

        // Generate a secure random password for Keycloak admin
        string keycloakAdminPassword = GenerateSecurePassword(24);
        var keycloakAdminPasswordSecret = factory.CreateSecret($"{name}-keycloak-admin-password", keycloakAdminPassword, ignoreChangesAfterCreate: true);

        // Create compute resource for Keycloak
        var compute = factory.CreateCompute(
            name: $"{name}-keycloak",
            registry: registry,
            imageTag: "latest",
            serviceType: null // Uses default per cloud
        );

        // Add required secrets and inject as environment variables
        compute.AddEnvironmentVariable("KEYCLOAK_ADMIN", "admin");
        compute.AddRequiredSecret(postgresUrlSecret, "KC_DB_URL");
        compute.AddRequiredSecret(postgresUserSecret, "KC_DB_USERNAME");
        compute.AddRequiredSecret(postgresPasswordSecret, "KC_DB_PASSWORD");
        compute.AddRequiredSecret(keycloakAdminPasswordSecret, "KEYCLOAK_ADMIN_PASSWORD");

        // Set KC_DB as a plain environment variable
        compute.AddEnvironmentVariable("KC_DB", "postgres");

        // Configure health check using cross-cloud abstraction
        compute.HealthCheckConfiguration = new HealthCheckConfiguration
        {
            Path = "/",
            IntervalSeconds = 3,
            TimeoutSeconds = 1,
            UnhealthyThreshold = 3,
            HealthyThreshold = 2,
            StartupTimeoutSeconds = 0,
            StartupIntervalSeconds = 3,
            StartupUnhealthyThreshold = 3
        };

        // Configure port forwarding for Keycloak
        compute.PortForwardings = [
            new PortForwardingConfiguration { ContainerPort = 8080 }
        ];

        // Set scaling options
        compute.MaxCount = 1;
        compute.Build();
    }

    /// <summary>
    /// Generates a secure random password of the specified length using .NET cryptography.
    /// </summary>
    private static string GenerateSecurePassword(int length)
    {
        const string allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-_=+";
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = allowedChars[bytes[i] % allowedChars.Length];
        }
        return new string(chars);
    }
}
