# PulumiCrossCloud.Bird

Bird (bird.com) email sending domains for PulumiCrossCloud. `BirdEmailService` implements
`IEmailService`. It creates the sending domain, can publish its DNS records in any `IDnsZone`,
waits for Bird to verify the domain, and exposes SMTP settings for Bird's relay.

## Usage

```csharp
var config = new Config();
var bird = new BirdResourceFactory(config.RequireSecret("birdApiKey"), BirdRegion.Us1);
var email = bird.CreateEmailService("prod-email", "mail.prod.example.com", cloudflareZone);

app.AddEnvironmentVariable("Email__Host", email.SmtpEndpoint);   // us1.smtp.bird.com
app.AddEnvironmentVariable("Email__Port", email.SmtpPort.Apply(p => p.ToString())); // 587
app.AddEnvironmentVariable("Email__Username", email.Username);   // bird
```

`email.Password` is the API key, held as a Pulumi secret. Its `ValueResource` is the verification
resource, so anything that consumes it is deployed after the domain can send. The SMTP password
needs the `emails` scope. Pass `BirdEmailServiceOptions.SmtpApiKey` to use a different key from
the one that manages domains.

`new BirdResourceFactory()` uses the default provider instead. Configure it with
`pulumi config set --secret bird:apiKey <key>` (or `BIRD_API_KEY`). The region defaults to the
one encoded in the key (`bk_us1_…`, `bk_eu1_…`). Set `bird:region` or `BIRD_REGION` to override it.

### Published records

| Record | Type | Notes |
| --- | --- | --- |
| DKIM | TXT | Proves ownership. |
| Return path | CNAME | Bounce domain; also covers SPF. Name part from `ReturnPathName` (Bird default `send`). |
| DMARC | TXT | Bird's suggested policy, or `v=DMARC1; p=none;`. Disable with `PublishDmarcRecord = false` when the zone already has one. |
| Tracking | CNAME | Optional. `TrackingName = ""` disables tracking and skips the record. |

Inbound MX records are never published, because they would redirect existing mail for the domain.

When a zone is supplied, `DomainVerification` waits until Bird reports that the domain can send.
The wait defaults to 20 minutes and honors `CustomTimeouts.Create`. If it times out, the error
lists each record that is still pending, with Bird's explanation. Set `WaitForVerification = false`
to skip waiting.

## Provider plugin

The resources are served by `pulumi-resource-bird`, a native Pulumi provider built with the .NET
provider SDK in `providers/PulumiCrossCloud.Bird.Provider`. Each release attaches the plugin for
Linux, macOS and Windows (x64 and arm64) to the GitHub release that has the same version as this
package, and Pulumi downloads it automatically.

To use a local build, publish it and point `Pulumi.yaml` at it:

```bash
dotnet publish providers/PulumiCrossCloud.Bird.Provider -c Release -o ./bird-plugin
```

```yaml
plugins:
  providers:
    - name: bird
      path: ./bird-plugin
```

`BIRD_API_URL` points the plugin at another API endpoint, such as a local fake for testing.

Resources:

- `bird:index:SendingDomain`. Changing `domain` replaces it. The name parts and tracking toggles
  update in place. Refresh detects drift, and an existing domain can be adopted with
  `pulumi import bird:index:SendingDomain <name> <dom_id>`.
- `bird:index:DomainVerification`. Triggers Bird's DNS check. A change to `triggers` (the
  published record values) runs it again. Deleting it does not change anything in Bird.
