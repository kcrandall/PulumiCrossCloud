# Writing a provider plugin

Add a native provider when a vendor has no Pulumi provider, or its bridged provider lacks a
resource you need. Prefer that over a `Pulumi.Command`: a provider previews, diffs property by
property, updates in place, detects drift on refresh and keeps credentials secret. Check first
whether the bridged provider, or a newer upstream release of it, already has the setting.

Each vendor gets its own plugin, so programs download only what they use. The existing plugins
are small; `providers/PulumiCrossCloud.Plugin.Neon` (one resource) is a good template.

## 1. The plugin project

Create `providers/PulumiCrossCloud.Plugin.<Name>/` with:

- `PulumiCrossCloud.Plugin.<Name>.csproj`: copy an existing one and change the
  `AssemblyName` to `pulumi-resource-crosscloud-<name>` (lowercase), which is the executable
  name Pulumi looks for. Keep `IsAotCompatible`, `InvariantGlobalization` and the
  `InternalsVisibleTo` for the tests.
- `Program.cs`: `Provider.Serve(args, PluginVersion.Current, _ => new <Name>ResourceProvider(), ...)`.
- `<Name>ApiClient.cs`: derive from `JsonApiClient`. It handles auth headers, sized JSON
  bodies, retries for 429 and 5xx (honoring `Retry-After`) and descriptive `ApiException`s.
  Pass `sendIdempotencyKeys: true` only if the API deduplicates `Idempotency-Key`; otherwise
  POSTs are never retried after a transport failure. Deserialize with a
  `JsonSerializerContext` (source generation), because reflection does not work under Native
  AOT. Use `JsonObject` when an update must send back fields you do not model.
- `<Name>ResourceProvider.cs`: derive from `ResourceProviderBase`. Register each resource
  handler in `Resources`, implement `CheckConfig` and `Configure`, and list config keys in
  `ConfigKeys`. Read credentials from config first, then an environment variable, and allow
  a `<NAME>_API_URL` override for tests via `BaseUri`. Override `ConfigReplaces` when a config
  change (such as region) moves resources.
- One `IResourceHandler` per resource type token `crosscloud-<name>:index:<Type>`:
  - `Check` validates inputs and returns failures instead of throwing.
  - `Diff` usually is `InputDiff.Compute(request, inputs, replaceInputs, requiredInputs)`.
  - `Create`/`Update` return computed outputs on preview and never call the API then.
  - `Read` returns an empty response when the object is gone, and puts live values into
    `Inputs` for the properties the user set, so refresh shows drift.
  - `Delete` decides whether destroying the resource changes anything in the vendor. Leave
    shared or pre-existing objects alone (document it) rather than deleting them.
  - Mark credential outputs with `.WithSecret(true)`; never put response bodies that hold
    secrets into exception messages.
  - Wait for asynchronous work (verification, deployments) with `VerificationPoller`, honoring
    `request.Timeout`.
- `<Name>Schema.cs`: the package schema as JSON. Name it `crosscloud-<name>`, set
  `pluginDownloadURL` to `github://api.github.com/kcrandall/PulumiCrossCloud`, and mark replace
  properties with `willReplaceOnChanges` and secrets with `"secret": true`.

Add the project to `PulumiCrossCloud.slnx` under the `providers` folder.

## 2. The C# SDK types

In the matching `src/PulumiCrossCloud.<Vendor>` package, write the resource and provider classes
by hand (see `NeonEndpointSettings.cs`):

- A `CustomResource` with `[Output]` properties matching the schema, constructed with
  `CrossCloudPlugins.WithDefaults(options)` so it requests the plugin version that matches the
  package and knows where to download it. Add `AdditionalSecretOutputs` for secret outputs.
- A `ResourceArgs` class with `[Input]` properties.
- A `ProviderResource` named `CrossCloud<Name>Provider` whose package is `crosscloud-<name>`,
  with secret credentials wrapped in `Output.CreateSecret`.

Use these from the package's components, and replace any `Pulumi.Command` they used. If the
old command created something that destroying it would remove (as Cloudflare's token command
did), give existing stacks a migration path rather than letting the upgrade recreate it; see
`scripts/migrate-cloudflare-account-tokens.py`.

## 3. Tests

In `tests/PulumiCrossCloud.Plugin.Tests`:

- Provider tests against `FakeApiHandler`, covering create, update in place, replacement, drift
  on read, delete, the vendor's error shape and preview.
- Add the resource and provider to `SchemaConsistencyTests`, which fails when the hand-written
  C# properties and the schema disagree.
- Component tests with Pulumi mocks for the package code that uses the resource.
- Optionally a `LiveApiTests` case, skipped unless its credentials are set.

Then run an end-to-end check with the real engine: publish the plugin, point a test project's
`Pulumi.yaml` `plugins.providers` entry at it, and run `up`, `refresh`, a change and `destroy`
against a fake HTTP server.

## 4. Release

Nothing to edit. `scripts/package-plugins.sh` and both workflows discover every
`providers/PulumiCrossCloud.Plugin.<Name>` project except `Common`, build it as Native AOT for
each platform, attach it to the GitHub release and check that `pulumi plugin install` can
download it. Add the plugin to the tables in [provider-plugins.md](provider-plugins.md) and to
the package README.
