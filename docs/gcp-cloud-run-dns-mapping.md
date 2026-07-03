# GCP Cloud Run DNS Mapping and SSL Certificates

This document explains how to use the new DNS mapping and automatic SSL certificate functionality in the GCP Cloud Run compute resource.

## Overview

The `GcpCloudRunCompute` class now includes a method `RegisterDnsMappingAndCertificates` that allows you to:
- Map custom domains to your Cloud Run service
- Automatically provision SSL certificates
- Get the DNS records you need to configure in your DNS provider

## Usage

### Basic Domain Mapping

```csharp
// Create your Cloud Run service
var cloudRunService = factory.CreateCompute("web-app", registry, "web-app-image", "latest", serviceType: ComputeServiceType.CloudRun);

// Register custom domains (can be called multiple times)
var customDomains1 = new[] { "app.example.com", "www.example.com" };
var dnsRecords1 = ((GcpCloudRunCompute)cloudRunService).RegisterDnsMappingAndCertificates(customDomains1);

// Register additional domains (duplicates will be ignored)
var customDomains2 = new[] { "api.example.com", "app.example.com" }; // app.example.com already exists, won't be duplicated
var dnsRecords2 = ((GcpCloudRunCompute)cloudRunService).RegisterDnsMappingAndCertificates(customDomains2);

// Build the service (this will create domain mappings for all unique domains)
cloudRunService.Build();
```

### What Happens

1. **DNS Records**: The method returns a list of `DnsRecord` objects that you need to configure in your DNS provider
2. **Domain Storage**: Custom domains are stored in a HashSet to prevent duplicates and allow multiple calls
3. **Domain Mappings**: When `Build()` is called, GCP Cloud Run domain mappings are created automatically for all registered domains
4. **SSL Certificates**: GCP automatically provisions and manages SSL certificates for your domains
5. **Resource Dependencies**: All resources are properly ordered and dependencies are managed

### DNS Records

The method returns `DnsRecord` objects with:
- **Type**: `CNAME` (Cloud Run uses CNAME records)
- **Name**: Your custom domain (e.g., "app.example.com")
- **Value**: The Cloud Run service URL (e.g., "web-app-abc123.a.run.app")

### Example DNS Configuration

For the domains `app.example.com` and `www.example.com`, you would configure these CNAME records in your DNS provider:

```
CNAME app.example.com -> web-app-abc123.a.run.app
CNAME www.example.com -> web-app-abc123.a.run.app
```

## Implementation Details

### Resource Creation

The method:
- Stores custom domains in a `HashSet<string>` to prevent duplicates
- Returns DNS records immediately for configuration
- Creates `DomainMapping` resources during `Build()` for each registered domain
- Ensures proper resource dependencies so Cloud Run service is created first
- Enables automatic SSL certificate provisioning through GCP

### Resource Access

You can access the created domain mappings through the `DomainMappings` property:

```csharp
var domainMappings = ((GcpCloudRunCompute)cloudRunService).DomainMappings;
foreach (var mapping in domainMappings)
{
    // Access individual domain mapping resources
    Console.WriteLine($"Domain mapping: {mapping.Name}");
}
```

## Best Practices

1. **Call Before Build**: Always call `RegisterDnsMappingAndCertificates` before calling `Build()`
2. **Multiple Calls Supported**: You can call the method multiple times with different domains - they will be added to the set
3. **DNS Configuration**: Configure the returned DNS records in your DNS provider before calling `Build()`
4. **Domain Validation**: GCP will automatically validate domain ownership through DNS
5. **SSL Provisioning**: SSL certificates are provisioned automatically and managed by GCP

## Limitations

- Only supports CNAME records (Cloud Run limitation)
- Domains must be valid and accessible
- SSL certificate provisioning may take several minutes
- Maximum of 100 domain mappings per service (GCP limit)

## Troubleshooting

### Common Issues

1. **DNS Not Propagated**: Wait for DNS propagation (can take up to 48 hours)
2. **SSL Certificate Pending**: SSL provisioning can take 10-60 minutes
3. **Domain Validation Failed**: Ensure DNS records are correctly configured

### Verification

You can verify the setup by:
1. Checking the Cloud Run console for domain mappings
2. Verifying DNS records resolve correctly
3. Testing HTTPS access to your custom domains

## Example Complete Stack

```csharp
public class GcpInfrastructureStack : Stack
{
    public GcpInfrastructureStack()
    {
        var factory = new GcpCloudResourceFactory();
        
        // Create resources
        var registry = factory.CreateContainerRegistry("app-registry");
        var cloudRunService = factory.CreateCompute("web-app", registry, "web-app-image", "latest", serviceType: ComputeServiceType.CloudRun);
        
        // Register custom domains
        var customDomains = new[] { "app.example.com", "www.example.com" };
        var dnsRecords = ((GcpCloudRunCompute)cloudRunService).RegisterDnsMappingAndCertificates(customDomains);
        
        // Build everything
        cloudRunService.Build();
        
        // Export values
        this.CloudRunUrl = ((GcpCloudRunCompute)cloudRunService).ServiceUrl;
        this.CustomDomains = Output.Create(customDomains);
        this.DnsRecords = Output.Create(dnsRecords.Select(r => $"{r.Type} {r.Name} -> {r.Value}"));
    }
    
    [Output] public Output<string> CloudRunUrl { get; set; }
    [Output] public Output<string[]> CustomDomains { get; set; }
    [Output] public Output<string[]> DnsRecords { get; set; }
}
```
