# PulumiCrossCloud.Bird

Bird (bird.com) email sending domains for PulumiCrossCloud. `BirdEmailService` implements
`IEmailService`. It creates the sending domain, can publish its DNS records in any `IDnsZone`,
waits for Bird to verify the domain, can turn on inbound mail, and exposes SMTP settings for
Bird's relay.

## Usage

```csharp
var config = new Config();
var bird = new BirdResourceFactory(config.RequireSecret("birdApiKey"), BirdRegion.Us1);
var email = bird.CreateEmailService("prod-email", "mail.prod.example.com", cloudflareZone);

app.AddEnvironmentVariable("Email__Host", email.SmtpEndpoint);   // us1.smtp.bird.com
app.AddEnvironmentVariable("Email__Port", email.SmtpPort.Apply(p => p.ToString())); // 587
app.AddEnvironmentVariable("Email__Username", email.Username);   // bird
```

`email.Password` is the API key, held as a Pulumi secret. `email.Ready` (and the password's
`ValueResource`) is the verification resource, so anything that depends on it deploys after the
domain can send. The SMTP password needs the `emails` scope.

Give the application its own key: create one in the dashboard (or `bird api-keys create`) with
only `emails:write`, and pass it as `BirdEmailServiceOptions.SmtpApiKey`. Pulumi cannot create
it, because Bird never lets an API key issue keys. The key that manages domains then stays with
the deployment and needs `domains:write`, plus `webhooks:write` and `email_management:write`
if you use webhooks or suppressions.

`new BirdResourceFactory()` uses the default provider instead. Configure it with
`pulumi config set --secret crosscloud-bird:apiKey <key>` (or `BIRD_API_KEY`). The region
defaults to the one encoded in the key (`bk_us1_…`, `bk_eu1_…`). Set `crosscloud-bird:region`
or `BIRD_REGION` to override it.

### Published records

| Record | Type | Notes |
| --- | --- | --- |
| DKIM | TXT | Proves ownership. |
| Return path | CNAME | Bounce domain; also covers SPF. Name part from `ReturnPathName` (Bird default `send`). |
| DMARC | TXT | Bird's suggested policy, or `v=DMARC1; p=none;`. Disable with `PublishDmarcRecord = false` when the zone already has one. |
| Tracking | CNAME | Optional. `TrackingName = ""` disables tracking and skips the record. |
| Inbound MX | MX | Only with `ReceivingEnabled`. See below. |

When a zone is supplied, `DomainVerification` waits until Bird reports that the domain can send.
The wait defaults to 20 minutes and honors `CustomTimeouts.Create`. If it times out, the error
lists each record that is still pending, with Bird's explanation. Set `WaitForVerification = false`
to skip waiting.

### Receiving mail

`ReceivingEnabled = true` publishes the domain's inbound MX records and creates a
`DomainReceiving` resource after verification (Bird requires DKIM to verify first). Mail to any
address at the domain then arrives as an inbound message. Use a dedicated subdomain, such as
`inbound.example.com`: MX records on a domain that already receives mail would redirect it.

DNS resources need fixed keys, so `InboundMxRecordCount` (default 3) sets how many MX records to
expect. If Bird reports a different number, deployment fails and names the actual count.
Destroying `DomainReceiving` turns receiving off and waits until Bird finishes, because Bird
refuses to delete a domain that still receives mail.

### Inbound mail to a URL

`factory.CreateEmailService(name, domain, zone, new InboundMailOptions { WebhookUrl = ... })`
(or `BirdEmailServiceOptions.InboundMail`) turns receiving on as above and adds a `Webhook`
for `email.received` events. `email.InboundMail.SigningSecret` holds its `whsec_` secret for
verifying deliveries; each event's `inbound_message_id` fetches the message from
`/v1/email/inbound-messages/{id}`. Bird webhook endpoints are workspace wide, so a handler
receives mail for every receiving domain in the workspace and should check the recipient.
`InboundMailOptions` is shared with Mailgun, so the same call works with either factory.

### Webhooks and suppressions

- `new Webhook(name, new WebhookArgs { Url = ..., Events = ["email.delivered", "email.bounced"] })`
  subscribes an endpoint to any event types. URL, events, description and `Paused` update in
  place. `Secret` is only known for endpoints created by Pulumi, because Bird returns it once.
- `new Suppression(name, new SuppressionArgs { Email = "..." })` stops all email to an address.
  Deleting it removes only that manual record.

Pass `new CustomResourceOptions { Provider = factory.ProviderResource }` when the factory created
an explicit provider.

### Other options

- `DefaultIpPoolId`: the IP pool (`ipp_…`) for sends that don't name one. An empty string
  restores normal organization routing.
- `GetSendingDomain.Invoke(...)` looks up an existing domain by name, for example to publish its
  records from another stack. Pass `new InvokeOptions { Provider = factory.ProviderResource }`
  when the factory created an explicit provider.

## Provider plugin

The resources are served by `pulumi-resource-crosscloud-bird`, a native Pulumi provider built
with the .NET provider SDK in `providers/PulumiCrossCloud.Plugin.Bird`. Pulumi downloads it
automatically from the GitHub release that matches this package's version. See
[native provider plugins](../../docs/provider-plugins.md) for distribution, local builds and tests.

Resources:

- `crosscloud-bird:index:SendingDomain`. Changing `domain` replaces it. The name parts, tracking
  toggles and IP pool update in place. Refresh detects drift, and an existing domain can be
  adopted with `pulumi import crosscloud-bird:index:SendingDomain <name> <dom_id>`.
- `crosscloud-bird:index:DomainVerification`. Triggers Bird's DNS check. A change to `triggers`
  (the published record values) runs it again. Deleting it does not change anything in Bird.
- `crosscloud-bird:index:DomainReceiving`. Turns inbound mail on, and off again when deleted.
- `crosscloud-bird:index:Webhook`. A webhook endpoint; everything updates in place.
- `crosscloud-bird:index:Suppression`. A manual suppression; changing the address replaces it.
- `crosscloud-bird:index:getSendingDomain`. Looks up a domain by name.
