# PulumiCrossCloud.Cloudflare

CloudFlare DNS zone and record management for PulumiCrossCloud.

## Authentication Setup

The CloudFlare provider requires an API token to authenticate. You have several options:

### Option 1: Environment Variable (Recommended)

```bash
export CLOUDFLARE_API_TOKEN="your-api-token"
```

### Option 2: Pulumi Config

```bash
pulumi config set cloudflare:apiToken --secret
# Enter your token when prompted
```

### Option 3: Custom Provider (Multi-Account)

For scenarios requiring multiple CloudFlare accounts or programmatic token management:

```csharp
var customProvider = new Pulumi.Cloudflare.Provider("my-provider", new()
{
    ApiToken = "your-api-token"
});

var factory = new CloudflareCloudResourceFactory(customProvider);
```

## Getting Your API Token

1. Go to [CloudFlare Dashboard → API Tokens](https://dash.cloudflare.com/profile/api-tokens)
2. Click **Create Token**
3. Use the **Edit zone DNS** template, or create a custom token with:
   - **Zone:DNS:Edit** - Create/update/delete DNS records
   - **Zone:Zone:Read** - Look up existing zones
4. Optionally restrict to specific zones for security
5. Copy the generated token

## Usage Examples

### Looking Up Existing Zones (Most Common)

No account ID needed — the API token already has access to your zones:

```csharp
// Uses CLOUDFLARE_API_TOKEN environment variable
var factory = new CloudflareCloudResourceFactory();

// Look up existing zone by domain name
var zone = factory.LookupDnsZone("my-zone", "example.com");

// Register DNS records
var records = apiApp.GetRequiredDnsRecords(["api.example.com"]);
zone.RegisterRecords(records, ttl: 300, proxied: true);
```

### Creating a New Zone

Account ID is only required when creating new zones (find it in CloudFlare Dashboard → select a domain → right sidebar under **API**):

```csharp
var factory = new CloudflareCloudResourceFactory(accountId: "your-account-id");
var zone = factory.CreateDnsZone("new-zone", "newdomain.com");
```

### Multi-Account Setup

```csharp
// Account A
var providerA = new Pulumi.Cloudflare.Provider("cf-account-a", new()
{
    ApiToken = accountAToken
});
var factoryA = new CloudflareCloudResourceFactory("account-a-id", providerA);

// Account B (lookup only, no account ID needed)
var providerB = new Pulumi.Cloudflare.Provider("cf-account-b", new()
{
    ApiToken = accountBToken
});
var factoryB = new CloudflareCloudResourceFactory(providerB);
```

## CloudFlare-Specific Features

### Proxied Records (Orange Cloud)

CloudFlare can proxy traffic through their CDN for A, AAAA, and CNAME records:

```csharp
zone.RegisterRecord(record, ttl: 300, proxied: true);
```

When `proxied: true`:
- Traffic routes through CloudFlare's CDN
- DDoS protection is enabled
- TTL is automatically set to "Auto" (1)
- Only works for A, AAAA, and CNAME records

### Supported Record Types

- A, AAAA (IPv4/IPv6 addresses)
- CNAME (aliases)
- TXT (text records)
- MX (mail exchange)
- SRV (service records)
- NS (nameserver)
- CAA (certificate authority authorization)
- PTR (pointer records)

## Account API tokens and R2 credentials

`AccountApiToken` creates an account-owned API token, and `CloudflareR2Credentials` derives
S3-compatible R2 keys from one scoped to a single bucket. Both use the native
`crosscloud-cloudflare` provider (`CloudflareAccountToken`), because the bridged provider only
creates user tokens, which stop working when that user leaves the account. The token passed in
(`primaryToken`) must be allowed to manage the account's API tokens.

- Changing the name or permissions updates the token in place and keeps its value.
- `pulumi refresh` detects revoked tokens and permission changes made in the dashboard.
- Destroying the resource revokes the token.

Stacks created before this provider existed used a shell command for these tokens. Run
`scripts/migrate-cloudflare-account-tokens.py` once before upgrading, or the upgrade replaces
and revokes them; see [upgrading](../../docs/upgrading.md).

