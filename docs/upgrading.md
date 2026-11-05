# Upgrading from 0.1.2-alpha.20260930 and earlier

These releases replaced shell commands (`Pulumi.Command`) with native provider plugins (see
[native provider plugins](provider-plugins.md)). Pulumi downloads the plugins from this
repository's GitHub releases. In CI, set `GITHUB_TOKEN` (any token; in GitHub Actions,
`${{ github.token }}`) so the downloads don't hit GitHub's anonymous rate limit.

Run `pulumi preview` after upgrading and before `pulumi up`. The sections below say what the
preview should show.

## Cloudflare account tokens and R2 credentials: migrate first

`AccountApiToken` and `CloudflareR2Credentials` now use `crosscloud-cloudflare:index:AccountToken`.
Without migrating, the upgrade would create new tokens and then revoke the old ones, which
rotates every R2 key and breaks anything still running with the old one. Before the first
`pulumi up` with the new release, run this for each stack that has them:

```bash
python3 scripts/migrate-cloudflare-account-tokens.py --stack prod          # dry run: lists the tokens
python3 scripts/migrate-cloudflare-account-tokens.py --stack prod --apply  # rewrites the state
```

It saves an encrypted backup, then moves each existing token into the new resource with the same
ID and value; nothing is created or revoked. `pulumi preview` should then show no token being
created or deleted (the stack's R2 secret-key output may change to `[secret]`). To undo, run
`pulumi stack import --file <backup>`. From then on, permission changes update a token in place
instead of replacing it.

## Mailgun: deployments now wait for verification

`MailgunEmailService` (and `MailgunResourceFactory`) now verifies the domain through
`crosscloud-mailgun:index:DomainVerification` whenever it publishes DNS records in a zone, and
waits until Mailgun reports the domain active (up to 20 minutes, or `CustomTimeouts.Create`).
`IEmailService.Ready` and the password's `ValueResource` become that verification, so anything
depending on them deploys after the domain can send.

- It uses the API key passed to the factory, or the `mailgun:apiKey` stack secret.
- An already-active domain verifies on the first check. Existing stacks see the verification
  and its provider as new resources in the preview, and nothing else changes in Mailgun.
- To keep the old behavior, set `WaitForVerification = false` on the factory, or pass
  `waitForVerification: false` to `MailgunEmailService`.

New, opt-in `MailgunEmailServiceOptions`: `ClickTracking`, `OpenTracking`, `SmtpLogin` (a
dedicated SMTP login instead of `postmaster`) and `InboundMail`.

## Neon compute settings: nothing to do

`NeonPostgresDatabase` compute settings (`MinCu`, `MaxCu`, `SuspendTimeoutSeconds`) now use
`crosscloud-neon:index:EndpointSettings`, and `python3` is no longer needed. The preview shows
the old command being deleted and the new resource being created. Deleting the old command
runs nothing, and the new resource only sends settings that differ from the live compute, so a
stack whose compute already matches sees no change in Neon. `ComputeSettings` keeps its JSON
format.

## Koyeb scale-to-zero: the token comes from KOYEB_TOKEN

`KoyebServiceCompute.ScaleToZeroOptions` now uses `crosscloud-koyeb:index:ServiceSleep` instead of
the `koyeb` CLI. The default provider reads `KOYEB_TOKEN` (or `crosscloud-koyeb:apiToken`), the
same token the bridged `koyeb` provider uses. The first deployment redeploys the service once
if its definition lacks the delays. After that, the delays are reapplied whenever the bridged
provider redeploys the service, which the old command did not do.

## Keycloak: readiness is checked in-process

`KeycloakStack` waits for `/health/ready` from the Pulumi program on every deployment instead of
running `curl` in `bash` once. The preview shows the old `wait-for-keycloak-*` command being
deleted; deleting it runs nothing.

## PostHog organizations

`PostHogOrganization` now uses `crosscloud-posthog:index:Organization` instead of a Python
command, and `RawJson` was removed; use `OrganizationId` and `Slug`. A stack that created one
with `DeleteOnDestroy = true` under the old release must run
`pulumi state delete <the organization command's URN>` first, or the old command's delete
script removes the organization.
