#!/usr/bin/env python3
"""Verify bundled providers and complete meta-package dependencies."""

import argparse
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
from zipfile import BadZipFile, ZipFile


PROVIDERS = {
    "Fly": "Pulumi.Fly",
    "Koyeb": "Pulumi.Koyeb",
    "Neon": "Pulumi.Neon",
    "PostHog": "Pulumi.Posthog",
    "Sentry": "Pulumi.Sentry",
    "Upstash": "Pulumi.Upstash",
}
GENERATED_DIRS = {
    f"PulumiCrossCloud.{wrapper}": Path(__file__).resolve().parents[1]
    / "src" / f"PulumiCrossCloud.{wrapper}" / "Generated" / wrapper.lower()
    for wrapper in PROVIDERS
}
LIBRARY_IDS = {
    directory.name
    for directory in (Path(__file__).resolve().parents[1] / "src").glob("PulumiCrossCloud.*")
    if (directory / f"{directory.name}.csproj").is_file()
}


def verify_packages(package_dir):
    errors = []
    generated_ids = {provider.casefold() for provider in PROVIDERS.values()}
    package_rules = {f"PulumiCrossCloud.{wrapper}": provider for wrapper, provider in PROVIDERS.items()}
    package_rules["PulumiCrossCloud"] = None
    for package_id, provider in package_rules.items():
        packages = sorted(
            path for path in package_dir.glob(f"{package_id}.[0-9]*.nupkg")
            if not path.name.endswith(".symbols.nupkg")
        )
        if not packages:
            errors.append(f"Missing {package_id} package in {package_dir}")
        for path in packages:
            try:
                with ZipFile(path) as package:
                    if provider:
                        assembly = f"lib/net10.0/{provider}.dll"
                        if assembly not in package.namelist() or package.getinfo(assembly).file_size == 0:
                            errors.append(f"{path.name}: missing bundled {assembly}")
                        for filename in ("pulumi-plugin.json", "version.txt"):
                            metadata_path = f"content/{filename}"
                            if metadata_path not in package.namelist():
                                errors.append(f"{path.name}: missing {metadata_path}")
                                continue
                            expected = (GENERATED_DIRS[package_id] / filename).read_text().strip()
                            actual = package.read(metadata_path).decode("utf-8").strip()
                            matches = (
                                json.loads(actual) == json.loads(expected)
                                if filename.endswith(".json") else actual == expected
                            )
                            if not matches:
                                errors.append(f"{path.name}: {metadata_path} differs from generated SDK metadata")
                    nuspecs = [name for name in package.namelist() if name.endswith(".nuspec")]
                    if len(nuspecs) != 1:
                        errors.append(f"{path.name}: expected exactly one .nuspec")
                        continue
                    nuspec = ET.fromstring(package.read(nuspecs[0]))
                    dependencies = {
                        dependency.get("id", "").casefold()
                        for dependency in nuspec.findall(".//{*}dependency")
                    }
                    for identifier in sorted(dependencies & generated_ids):
                        errors.append(f"{path.name}: external generated-provider dependency {identifier}")
                    if provider is None:
                        for library_id in sorted(LIBRARY_IDS):
                            if library_id.casefold() not in dependencies:
                                errors.append(f"{path.name}: missing direct dependency {library_id}")
            except (BadZipFile, ET.ParseError, OSError, UnicodeError, json.JSONDecodeError) as error:
                errors.append(f"{path.name}: {error}")
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "package_dir", nargs="?", type=Path,
        default=Path(__file__).resolve().parents[1] / "artifacts" / "nuget",
    )
    errors = verify_packages(parser.parse_args().package_dir)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print("Verified all six bundled providers, discovery metadata, and complete meta-package dependencies.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
