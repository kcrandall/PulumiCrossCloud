# Dependency and compatibility checks

The library and samples target .NET 10. The `Build and test` workflow builds the complete
solution and runs unit tests on Linux, Windows, and macOS. AWS and GCP integration tests run
against local Floci emulators on Linux; they do not deploy to live cloud accounts. See
[integration tests](integration-tests.md) for the resources covered and emulator limitations.
The Linux compatibility job also packs the NuGet packages and verifies that the Fly, Koyeb,
Neon, PostHog, Sentry, and Upstash wrappers bundle their generated provider assemblies without
requiring separately published provider packages. It also checks that the meta-package directly
depends on every library under `src/`, so installing the full package includes all providers.

## Dependency update (2026-10-27)

NuGet references were updated to their latest available stable releases. This includes the
Pulumi GCP provider's major update from version 9 to 10.2.0. Pulumi DockerBuild now uses
the maintained stable `0.0.22` release in place of the prior preview dependency; stable
PulumiCrossCloud packages can therefore be packed without prerelease-dependency warnings.

Pulumi.Command was corrected from the deprecated, incorrectly numbered `4.5.0` package
to the maintained `1.2.1` release. xUnit remains on its latest v2 release (`2.9.3`),
which receives security fixes; migrating to v3 requires a separate test framework migration.

The image build fix gives image resources their owning component as a parent and assigns
unique names to provider resources. Existing stacks may therefore preview image resource
replacements because their Pulumi URNs change. Review `pulumi preview` before upgrading an
existing deployment, particularly when also adopting the GCP provider major version update.
The compatibility checks exercise builds, tests, packages, and local emulators; live-cloud
deployment behavior still requires a preview in the target stack.

## Issues fixed

- Corrected SNS-to-SQS `aws:SourceArn` policy conditions and kept public S3/CDN grants consistent without removing existing policy resource identities.
- Made Cloud Run use the pushed image URL, wait for secret values, and support one secret mapped to multiple environment variables.
- Prevented image/provider resource collisions when building multiple images or running multiple deployments in one process.
- Fixed Azure storage naming for hyphenated bucket names, Fly secret values, Fly/Koyeb CNAME targets, and Apple private-key PEM parsing.
- Fixed repository discovery for Git worktrees and unavailable source paths, migration ID detection, and EF startup builds and redirected-output deadlocks.
- Bundled the generated Fly and Koyeb SDK assemblies in their NuGet packages, matching the other generated-provider wrappers, and restored DigitalOcean to the full meta-package.
- Corrected release version handling, package-test filtering, and Docker manifest digest comparisons.

## Verification on 2026-10-27

The full solution, including all samples, builds in Release with warnings treated as errors.
All 92 C# unit tests, 25 AWS/GCP emulator integration tests, 6 Neon Python tests, and
22 script tests passed on macOS ARM64 with .NET SDK 10.0.301. All 19 library packages
were packed, and all six bundled-provider wrappers passed package-content verification.
A fresh NuGet consumer compiled and loaded all 25 package assemblies and their public API types.
NuGet's vulnerability audit found no known vulnerabilities, including transitive dependencies.
CSharpier, actionlint, shellcheck, and whitespace checks passed.

GitHub Actions runs the same build and unit suite on Linux, Windows, and macOS, plus
Linux emulator tests and package verification. Azure integration and live cloud compute/database
deployments remain outside the emulator coverage described in the integration test documentation.

## Second audit (2026-10-28)

The follow-up review corrected additional deployment paths:

- Cloudflare token creation and R2 uploads now pass credentials and payloads through secret environment inputs, protect command output, and keep values out of shell source. Token deletion targets the exact previously created token ID. Windows commands select PowerShell explicitly; Windows CI executes literal-argument and failure-propagation regressions.
- PostgreSQL connection URLs on AWS, Azure, GCP, and Neon, plus Redis URLs for Upstash and PostHog, escape reserved characters in credentials and database names.
- RDS/Aurora connection policies use database-user ARNs; ECS Exec policies use cluster/task ARNs and restrict the container name.
- Cloud Run builds are idempotent, and readiness/health callbacks wait for the built service and run once per registration.
- Image URLs include the exported digest so changed build settings or base images can roll out even when the source tag stays the same. Image and bucket source hashes frame file names and contents; Docker ignore exceptions, wildcard depth, and Dockerfile-specific ignore files are respected by source tagging.
- Mailgun SMTP secrets depend on the created domain. All six generated-provider wrappers now include discovery metadata, which enables Pulumi plugin preinstallation and offline preparation.

Source hash framing changes source tags and bucket build/sync triggers once on upgrade.
Digest-qualified image URLs can also cause an initial compute rollout; review the target stack preview.

The follow-up Release build passed with warnings treated as errors. All 128 C# unit tests,
26 release/package script tests, and 6 Neon Python tests passed locally. All 25 AWS/GCP
emulator integration tests passed; rebuilt packages passed metadata verification, and a fresh
consumer loaded all 25 assemblies.

### Remaining GCP database networking limitation

`GcpPostgresOptions` does not yet expose a VPC/private network. `GcpPostgresDatabase`
disables public IPv4 by default without configuring a private network and always exposes
`PublicIpAddress` as its host. Consequently the default private Cloud SQL path is incomplete.
A separate networking change must accept the target private network and choose the corresponding
private address; this audit preserves the existing network exposure behavior.

## Local checks

```bash
dotnet restore PulumiCrossCloud.slnx
dotnet build PulumiCrossCloud.slnx --configuration Release --no-restore
dotnet test PulumiCrossCloud.slnx --configuration Release --no-build --filter 'Category!=Integration'
python3 -m unittest discover -s tests/PulumiCrossCloud.Neon.Tests -p 'test_*.py'
python3 -m unittest discover -s scripts/tests -p 'test_*.py'
bash scripts/pack.sh --prerelease stable
python3 scripts/verify-packages.py
```

Run `dotnet test tests/PulumiCrossCloud.Integration.Tests` separately when Docker and the
Pulumi CLI are available, or pass `--include-integration-tests` to `scripts/pack.sh`.
