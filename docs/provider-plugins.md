# Native provider plugins

Some services have no Pulumi provider, or their Terraform-bridged provider lacks a resource we
need. For those, this repository builds small native providers with the Pulumi .NET provider SDK.
Each is a separate plugin, so a program downloads only the ones it uses.

| Plugin | Package | Resources |
| --- | --- | --- |
| `pulumi-resource-crosscloud-bird` | [PulumiCrossCloud.Bird](../src/PulumiCrossCloud.Bird/README.md) | `crosscloud-bird:index:SendingDomain`, `DomainVerification`, `DomainReceiving`, `getSendingDomain` |
| `pulumi-resource-crosscloud-posthog` | PulumiCrossCloud.PostHog | `crosscloud-posthog:index:Organization` |
| `pulumi-resource-crosscloud-mailgun` | PulumiCrossCloud.Mailgun | `crosscloud-mailgun:index:DomainVerification` |

The `crosscloud-` prefix keeps them distinct from the official `posthog` and `mailgun` providers,
which the same packages also use for the resources those providers do support.

Unlike `Pulumi.Command` resources, these providers implement the full lifecycle. Inputs are
checked during preview, each property is either updated in place or replaces the resource,
refresh detects drift, existing objects can be imported, and API keys stay Pulumi secrets.

## Configuration

Packages create explicit providers from the credentials passed to their factories. Resources
used directly fall back to each plugin's default provider:

| Plugin | Stack config | Environment |
| --- | --- | --- |
| crosscloud-bird | `crosscloud-bird:apiKey`, `crosscloud-bird:region` (`us1`/`eu1`) | `BIRD_API_KEY`, `BIRD_REGION` |
| crosscloud-posthog | `crosscloud-posthog:apiKey`, `crosscloud-posthog:host` | `POSTHOG_API_KEY`, `POSTHOG_HOST` |
| crosscloud-mailgun | `crosscloud-mailgun:apiKey`, `crosscloud-mailgun:region` (`us`/`eu`) | `MAILGUN_API_KEY`, `MAILGUN_REGION` |

`BIRD_API_URL` and `MAILGUN_API_URL` point a plugin at another endpoint, such as a local fake.

## Distribution

The plugins are published as Native AOT executables (about 8 MB compressed, no .NET runtime
needed). The NuGet publish workflow builds them for Linux, macOS and Windows on x64 and arm64,
attaches them to the GitHub release `v<version>`, and checks that `pulumi plugin install` can
download each one before it pushes the NuGet packages. Resources request the plugin version
equal to their package version from `github://api.github.com/kcrandall/PulumiCrossCloud`.

Build archives locally with:

```bash
scripts/package-plugins.sh --version 0.1.3-alpha.1 --rid osx-arm64 --output artifacts/plugins
```

Native AOT compiles for the host OS only. Pass `--framework-dependent` for a small executable
that runs on any OS with the .NET 10 runtime installed. Single-file compression stays off,
because compressed bundles crashed in `HttpConnectionPool` under concurrent requests.

## Using a local build

Publish a plugin and point the project's `Pulumi.yaml` at it. Pulumi then uses it instead of
downloading one:

```bash
dotnet publish providers/PulumiCrossCloud.Plugin.Bird -c Release -o ./plugins/bird
```

```yaml
plugins:
  providers:
    - name: crosscloud-bird
      path: ./plugins/bird
```

## Tests

`tests/PulumiCrossCloud.Plugin.Tests` covers each provider against a fake HTTP handler, checks
the packages' resources and providers against each plugin's schema (the C# types are written by
hand), and exercises the email services with Pulumi mocks.

The `LiveApi` tests run against the real APIs and skip unless their variables are set. CI runs
them on `main`:

| Test | Variables |
| --- | --- |
| Bird domain lifecycle (creates and deletes a throwaway domain) | `BIRD_API_KEY` secret, `BIRD_TEST_DOMAIN_SUFFIX` variable (a domain you control) |
| Mailgun verification (one check, no waiting) | `MAILGUN_API_KEY` secret, `MAILGUN_TEST_DOMAIN` variable (an existing domain) |
