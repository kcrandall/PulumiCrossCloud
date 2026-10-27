# Scripts

Helper scripts used during development and package release workflows.

- `pack.sh`: restores, builds, runs unit tests, and packs the PulumiCrossCloud NuGet packages into `artifacts/nuget/`. Use `--include-integration-tests` to also run the emulator suites (requires Docker and the Pulumi CLI), or `--skip-tests` to skip tests.
- `publish-nuget.sh`: publishes packed packages to NuGet or another configured feed.
- `resolve-version.sh`: resolves the lockstep package version used during publishing.
- `verify-packages.py`: verifies that all six wrapper NuGet packages bundle their generated provider DLL under `lib/net10.0` without separate generated-provider package dependencies, and that the meta-package directly includes every library under `src/`. Run `python3 scripts/verify-packages.py` after packing, or pass a package directory as its argument.
- `check-image-digest.sh`: compares remote and local container manifest digests, including multi-platform indexes (requires Docker with Buildx).
- `tf-to-pulumi.sh`: runs Terraform-to-Pulumi conversion helpers.

Run the release script regression tests without building .NET projects:

```bash
python3 -m unittest discover -s scripts/tests -p 'test_*.py'
```
