# Scripts

Helper scripts used during development and package release workflows.

- `pack.sh`: restores, builds, optionally tests, and packs the PulumiCrossCloud NuGet packages into `artifacts/nuget/`.
- `publish-nuget.sh`: publishes packed packages to NuGet or another configured feed.
- `resolve-version.sh`: resolves the lockstep package version used during publishing.
- `check-image-digest.sh`: checks container image digest values.
- `tf-to-pulumi.sh`: runs Terraform-to-Pulumi conversion helpers.
