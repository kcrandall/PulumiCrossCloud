"""Exercise release decisions without building or publishing any packages."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPO_DIR = Path(__file__).resolve().parents[2]


class ReleaseScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        (self.repo / "scripts").mkdir()
        for script in ("pack.sh", "resolve-version.sh", "publish-nuget.sh"):
            shutil.copy2(REPO_DIR / "scripts" / script, self.repo / "scripts" / script)
        (self.repo / "Directory.Build.props").write_text(
            "<Project><PulumiCrossCloudVersion>1.2.3</PulumiCrossCloudVersion></Project>\n"
        )
        self.env = os.environ.copy()
        for key in ("GITHUB_ACTIONS", "GITHUB_REF_TYPE", "GITHUB_REF_NAME", "NUGET_API_KEY"):
            self.env.pop(key, None)

    def run_script(self, name, *args):
        return subprocess.run(
            ["bash", str(self.repo / "scripts" / name), *args],
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
        )

    def resolve(self, version, channel="alpha"):
        result = self.run_script(
            "resolve-version.sh", "--version", version, "--prerelease", channel
        )
        self.assertEqual(0, result.returncode, result.stderr)
        return result.stdout.strip()

    def test_existing_numbered_channel_is_not_duplicated(self):
        self.assertEqual("1.2.3-alpha.1", self.resolve("1.2.3-alpha.1"))
        self.assertEqual("1.2.3-beta.2", self.resolve("1.2.3-beta.2", "beta"))

    def test_prerelease_is_inserted_before_build_metadata(self):
        self.assertEqual("1.2.3-alpha+build.001", self.resolve("1.2.3+build.001"))
        self.assertEqual("1.2.3-alpha.1+build", self.resolve("1.2.3-alpha.1+build"))
        self.assertEqual("1.2.3-alpha.rc.1+build", self.resolve("1.2.3-rc.1+build"))

    def test_stable_preserves_an_explicit_resolved_version(self):
        self.assertEqual("1.2.3", self.resolve("1.2.3", "stable"))
        self.assertEqual("1.2.3-alpha.1", self.resolve("1.2.3-alpha.1", "stable"))

    def test_invalid_versions_are_rejected(self):
        for version in ("1.2", "01.2.3", "1.2.3-alpha.01", "1.2.3+", "1.2.3\ninjected=true"):
            with self.subTest(version=version):
                result = self.run_script("resolve-version.sh", "--version", version)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", result.stdout)

    def test_stable_ci_build_does_not_become_a_timestamp_prerelease(self):
        self.env["GITHUB_ACTIONS"] = "true"
        result = self.run_script("resolve-version.sh", "--prerelease", "stable")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("1.2.3", result.stdout.strip())

    def test_prerelease_ci_build_includes_timestamp(self):
        self.env["GITHUB_ACTIONS"] = "true"
        result = self.run_script("resolve-version.sh", "--prerelease", "beta")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertRegex(result.stdout.strip(), r"^1\.2\.3-beta\.[0-9]{14}$")

    def prepare_pack(self):
        (self.repo / "PulumiCrossCloud.slnx").write_text("<Solution />\n")
        source = self.repo / "src" / "PulumiCrossCloud.Core"
        source.mkdir(parents=True)
        (source / "PulumiCrossCloud.Core.csproj").write_text("<Project />\n")
        test_dir = self.repo / "tests" / "PulumiCrossCloud.Neon.Tests"
        test_dir.mkdir(parents=True)
        # Python 3.14 fails unittest discovery when it finds no tests. Keep this
        # fixture valid while the outer tests inspect only the dotnet arguments.
        (test_dir / "test_fixture.py").write_text(
            "import unittest\n"
            "class FixtureTests(unittest.TestCase):\n"
            "    def test_fixture(self):\n"
            "        pass\n"
        )
        executable_dir = self.repo / "bin"
        executable_dir.mkdir()
        dotnet = executable_dir / "dotnet"
        # Log one JSON array per invocation to preserve argument boundaries.
        dotnet.write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, sys\n"
            "with open(os.environ['DOTNET_CALL_LOG'], 'a') as log:\n"
            "    log.write(json.dumps(sys.argv[1:]) + '\\n')\n"
        )
        dotnet.chmod(0o755)
        self.log = self.repo / "dotnet-calls.jsonl"
        self.env["DOTNET_CALL_LOG"] = str(self.log)
        self.env["PATH"] = str(executable_dir) + os.pathsep + self.env["PATH"]

    def pack_calls(self, *args):
        self.prepare_pack()
        result = self.run_script("pack.sh", *args)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def test_pack_runs_unit_tests_without_emulator_prerequisites(self):
        test_call = next(call for call in self.pack_calls() if call[0] == "test")
        self.assertEqual("Category!=Integration&Category!=LiveApi", test_call[test_call.index("--filter") + 1])

    def test_pack_can_include_integration_tests(self):
        test_call = next(
            call for call in self.pack_calls("--include-integration-tests") if call[0] == "test"
        )
        self.assertNotIn("--filter", test_call)

    def test_pack_can_skip_all_tests(self):
        self.assertFalse(any(call[0] == "test" for call in self.pack_calls("--skip-tests")))

    def test_publish_dry_run_needs_no_credentials_and_excludes_legacy_symbols(self):
        packages = self.repo / "package directory"
        packages.mkdir()
        for name in ("Core.1.2.3.nupkg", "Core.1.2.3.snupkg", "Core.1.2.3.symbols.nupkg"):
            (packages / name).touch()
        result = self.run_script("publish-nuget.sh", "--package-dir", str(packages), "--dry-run")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Core.1.2.3.nupkg", result.stdout)
        self.assertNotIn(".snupkg", result.stdout)
        self.assertNotIn(".symbols.nupkg", result.stdout)


if __name__ == "__main__":
    unittest.main()
