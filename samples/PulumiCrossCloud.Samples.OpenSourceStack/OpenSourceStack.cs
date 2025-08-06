using Pulumi;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Gcp;
using PulumiCrossCloud.Aws;
using PulumiCrossCloud.Keycloak;

namespace PulumiCrossCloud.Samples.OpenSourceStack;

public class OpenSourceStack : Stack
{
    public OpenSourceStack()
    {
        // Example: Deploy a simple web app to all three clouds
        var gcpFactory = new GcpCloudResourceFactory();

        var gcpDbUrlSecret = gcpFactory.CreateSecret("keycloak-db-url", "jdbc:postgresql://localhost:5432/keycloak");
        var gcpDbUserSecret = gcpFactory.CreateSecret("keycloak-db-user", "admin");
        var gcpDbPasswordSecret = gcpFactory.CreateSecret("keycloak-db-password", "password");
        var keycloakStack = new KeycloakStack(
            gcpFactory,
            "oss-gcp-keycloak",
            gcpDbUrlSecret,
            gcpDbUserSecret,
            gcpDbPasswordSecret
        );
    }
}
