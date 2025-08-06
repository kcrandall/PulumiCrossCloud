using Pulumi;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Keycloak;

/// <summary>
/// KeycloakStack provisions a Keycloak instance using the cross-cloud abstractions.
/// </summary>
public class KeycloakStack : Stack
{
    public KeycloakStack(CloudResourceFactory factory, string name)
    {
        // Create container registry for Keycloak
        var registry = factory.CreateContainerRegistry($"{name}-keycloak-registry");

        // Create secret for Keycloak admin credentials
        var keycloakSecret = factory.CreateSecret($"{name}-keycloak-admin", "{\"username\":\"admin\",\"password\":\"default_admin_password\"}");

        // Create secret for Postgres connection string
        var postgresSecret = factory.CreateSecret($"{name}-postgres-connection", "Host=localhost;Port=5432;Username=postgres;Password=changeme;Database=keycloak");

        // Create compute resource for Keycloak
        var compute = factory.CreateCompute(
            name: $"{name}-keycloak",
            registry: registry,
            imageTag: "latest",
            serviceType: null // Uses default per cloud
        );

        // Add required secrets and inject as environment variables
        compute.AddRequiredSecret(keycloakSecret, "KEYCLOAK_SECRET");
        compute.AddRequiredSecret(postgresSecret, "POSTGRES_SECRET");

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
        compute.MinCount = 0;
        compute.MaxCount = 1;
    }
}
