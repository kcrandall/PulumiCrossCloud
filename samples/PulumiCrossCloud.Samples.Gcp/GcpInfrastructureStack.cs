using Pulumi;
using PulumiCrossCloud.Core;
using PulumiCrossCloud.Gcp;

namespace PulumiCrossCloud.Samples.Gcp;

public class GcpInfrastructureStack : Stack
{
    public GcpInfrastructureStack()
    {
        // Set GCP config programmatically for the sample
        // GcpProjectConfig.ProjectId = "your-project-id";
        // GcpProjectConfig.Region = "us-central1";
        // GcpProjectConfig.Zone = "us-central1-a";

        // Initialize the GCP cloud resource factory
        var factory = new GcpCloudResourceFactory();

        // Create resources
        var secret = factory.CreateSecret(
            "app-secret",
            "my-secret-value",
            ignoreChangesAfterCreate: true
        );
        var registry = factory.CreateContainerRegistry("app-registry", createDefaultImage: true);
        var cloudRunService = factory.CreateCompute(
            "web-app",
            registry,
            "web-app-image",
            "latest",
            serviceType: ComputeServiceType.CloudRun
        );

        // Add secret dependency
        ((GcpCloudRunCompute)cloudRunService).AddRequiredSecret(secret);

        // Example: Register custom domains with automatic SSL certificates
        // This will create domain mappings and return the DNS records you need to configure
        var customDomains = new[] { "app.example.com", "www.example.com" };
        var dnsRecords = ((GcpCloudRunCompute)cloudRunService).RegisterDnsMappingAndCertificates(
            customDomains
        );

        // Export values - using public properties and Output types
        this.CloudRunUrl = ((GcpCloudRunCompute)cloudRunService).ServiceUrl;
        this.RegistryUrl = ((GcpContainerRegistry)registry).RegistryUrlOutput;
        this.CustomDomains = Output.Create(customDomains);
        this.DnsRecords = Output.Create(
            dnsRecords.Select(r => $"{r.Type} {r.Name} -> {r.Value}").ToArray()
        );
    }

    [Output]
    public Output<string> CloudRunUrl { get; set; }

    [Output]
    public Output<string> RegistryUrl { get; set; }

    [Output]
    public Output<string[]> CustomDomains { get; set; }

    [Output]
    public Output<string[]> DnsRecords { get; set; }
}
