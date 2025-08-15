using Pulumi;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Gcp;
using PulumiCrossCloud.Aws;
using PulumiCrossCloud.Keycloak;

namespace PulumiCrossCloud.Samples.OpenSourceStack;

public class OpenSourceStack
{
    public OpenSourceStack()
    {
        // Example: Deploy a simple web app to all three clouds
        var gcpFactory = new GcpCloudResourceFactory();
        
        GcpProjectConfig.ProjectId = "pulumi-test-469022";

        var gcpDbUrlSecret = gcpFactory.CreateSecret("keycloak-db-url", "jdbc:postgresql://ep-lingering-rice-a5p2as68-pooler.us-east-2.aws.neon.tech:5432/keycloak?sslmode=require&channel_binding=require");
        var gcpDbUserSecret = gcpFactory.CreateSecret("keycloak-db-user", "main_owner");
        var gcpDbPasswordSecret = gcpFactory.CreateSecret("keycloak-db-password", "rcEj7YHeBQO0");
        var keycloakStack = new KeycloakStack(
            gcpFactory,
            "oss-gcp-keycloak",
            gcpDbUrlSecret,
            gcpDbUserSecret,
            gcpDbPasswordSecret
        );
    }
}
