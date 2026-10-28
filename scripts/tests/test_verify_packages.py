"""Catch package layouts that build locally but fail for NuGet consumers."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile


SCRIPT = Path(__file__).resolve().parents[1] / "verify-packages.py"
SPEC = importlib.util.spec_from_file_location("verify_packages", SCRIPT)
verifier = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verifier)


class VerifyPackagesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        for wrapper, provider in verifier.PROVIDERS.items():
            self.write_package(wrapper, provider)
        self.write_meta_package(verifier.LIBRARY_IDS)

    def write_meta_package(self, library_ids):
        with ZipFile(self.directory / "PulumiCrossCloud.1.2.3.nupkg", "w") as package:
            dependencies = "".join(
                f'<dependency id="{library_id}" version="1.2.3" />'
                for library_id in sorted(library_ids)
            )
            package.writestr(
                "PulumiCrossCloud.nuspec",
                '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
                '<metadata><dependencies><group targetFramework="net10.0">'
                f'{dependencies}</group></dependencies></metadata></package>',
            )

    def write_package(
        self, wrapper, provider, *, bundled=True, dependency="PulumiCrossCloud.Core",
        include_metadata=True, plugin_metadata=None, version=None,
    ):
        path = self.directory / f"PulumiCrossCloud.{wrapper}.1.2.3.nupkg"
        with ZipFile(path, "w") as package:
            if bundled:
                package.writestr(f"lib/net10.0/{provider}.dll", b"provider assembly")
            if include_metadata:
                generated_dir = verifier.GENERATED_DIRS[f"PulumiCrossCloud.{wrapper}"]
                package.writestr(
                    "content/pulumi-plugin.json",
                    plugin_metadata if plugin_metadata is not None
                    else (generated_dir / "pulumi-plugin.json").read_text(),
                )
                package.writestr(
                    "content/version.txt",
                    version if version is not None else (generated_dir / "version.txt").read_text(),
                )
            package.writestr(
                f"PulumiCrossCloud.{wrapper}.nuspec",
                '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
                '<metadata><dependencies><group targetFramework="net10.0">'
                f'<dependency id="{dependency}" version="1.2.3" />'
                '</group></dependencies></metadata></package>',
            )
        return path

    def test_all_six_bundled_providers_pass(self):
        self.assertEqual([], verifier.verify_packages(self.directory))

    def test_missing_wrapper_fails(self):
        (self.directory / "PulumiCrossCloud.Fly.1.2.3.nupkg").unlink()
        self.assertTrue(any("Missing PulumiCrossCloud.Fly" in error for error in verifier.verify_packages(self.directory)))

    def test_missing_provider_assembly_fails(self):
        self.write_package("Fly", "Pulumi.Fly", bundled=False)
        self.assertTrue(any("missing bundled lib/net10.0/Pulumi.Fly.dll" in error for error in verifier.verify_packages(self.directory)))

    def test_external_generated_provider_dependency_fails(self):
        self.write_package("Koyeb", "Pulumi.Koyeb", dependency="pulumi.koyeb")
        self.assertTrue(any("external generated-provider dependency pulumi.koyeb" in error for error in verifier.verify_packages(self.directory)))

    def test_missing_plugin_discovery_metadata_fails(self):
        self.write_package("Fly", "Pulumi.Fly", include_metadata=False)
        errors = verifier.verify_packages(self.directory)
        self.assertTrue(any("missing content/pulumi-plugin.json" in error for error in errors))
        self.assertTrue(any("missing content/version.txt" in error for error in errors))

    def test_incorrect_parameterized_provider_metadata_fails(self):
        source = verifier.GENERATED_DIRS["PulumiCrossCloud.Fly"] / "pulumi-plugin.json"
        for field, value in (("name", "wrong-provider"), ("version", "99.0.0"), ("value", "wrong-parameter")):
            with self.subTest(field=field):
                metadata = json.loads(source.read_text())
                metadata["parameterization"][field] = value
                self.write_package("Fly", "Pulumi.Fly", plugin_metadata=json.dumps(metadata))
                self.assertTrue(any(
                    "content/pulumi-plugin.json differs from generated SDK metadata" in error
                    for error in verifier.verify_packages(self.directory)
                ))

    def test_incorrect_generated_provider_version_fails(self):
        self.write_package("Fly", "Pulumi.Fly", version="99.0.0")
        self.assertTrue(any(
            "content/version.txt differs from generated SDK metadata" in error
            for error in verifier.verify_packages(self.directory)
        ))

    def test_invalid_plugin_json_fails(self):
        self.write_package("Fly", "Pulumi.Fly", plugin_metadata="not json")
        self.assertTrue(any("PulumiCrossCloud.Fly.1.2.3.nupkg:" in error for error in verifier.verify_packages(self.directory)))

    def test_meta_package_must_directly_include_every_library(self):
        self.assertIn("PulumiCrossCloud.DigitalOcean", verifier.LIBRARY_IDS)
        self.write_meta_package(verifier.LIBRARY_IDS - {"PulumiCrossCloud.DigitalOcean"})
        self.assertEqual(
            ["PulumiCrossCloud.1.2.3.nupkg: missing direct dependency PulumiCrossCloud.DigitalOcean"],
            verifier.verify_packages(self.directory),
        )

    def test_missing_meta_package_fails(self):
        (self.directory / "PulumiCrossCloud.1.2.3.nupkg").unlink()
        self.assertTrue(any("Missing PulumiCrossCloud package" in error for error in verifier.verify_packages(self.directory)))


if __name__ == "__main__":
    unittest.main()
