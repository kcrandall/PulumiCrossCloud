"""Check digest comparisons without downloading images or using a Docker daemon."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "check-image-digest.sh"


class ImageDigestTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        directory = Path(self.temp.name)
        docker = directory / "docker"
        docker.write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, sys\n"
            "args = sys.argv[1:]\n"
            "with open(os.environ['DOCKER_CALL_LOG'], 'a') as log:\n"
            "    log.write(json.dumps(args) + '\\n')\n"
            "if args[:3] == ['buildx', 'imagetools', 'inspect']:\n"
            "    assert args[4:] == ['--format', '{{.Manifest.Digest}}']\n"
            "    print(os.environ['MOCK_REMOTE_DIGEST'])\n"
            "elif args[:2] == ['image', 'inspect']:\n"
            "    print(os.environ['MOCK_LOCAL_REPO_DIGESTS'])\n"
            "else:\n"
            "    sys.exit(1)\n"
        )
        docker.chmod(0o755)
        self.log = directory / "docker-calls.jsonl"
        self.env = {
            **os.environ,
            "PATH": str(directory) + os.pathsep + os.environ["PATH"],
            "DOCKER_CALL_LOG": str(self.log),
            "MOCK_REMOTE_DIGEST": "sha256:manifest",
            "MOCK_LOCAL_REPO_DIGESTS": "registry.test/app@sha256:manifest",
        }

    def run_script(self, *args):
        return subprocess.run(
            ["bash", str(SCRIPT), *args],
            env=self.env,
            capture_output=True,
            text=True,
            check=False,
        )

    def result_line(self):
        result = self.run_script("app", "registry.test/")
        self.assertEqual(0, result.returncode, result.stderr)
        return result.stdout.strip().splitlines()[-1]

    def test_compares_the_remote_manifest_digest(self):
        self.assertEqual("match:sha256:manifest", self.result_line())
        calls = [json.loads(line) for line in self.log.read_text().splitlines()]
        self.assertEqual("registry.test/app:latest", calls[0][3])
        self.assertEqual("registry.test/app:latest", calls[1][2])

    def test_index_digest_can_match_any_local_repo_digest(self):
        self.env["MOCK_REMOTE_DIGEST"] = "sha256:index"
        self.env["MOCK_LOCAL_REPO_DIGESTS"] = (
            "other.test/app@sha256:other\nregistry.test/app@sha256:index"
        )
        self.assertEqual("match:sha256:index", self.result_line())

    def test_changed_manifest_is_reported(self):
        self.env["MOCK_LOCAL_REPO_DIGESTS"] = "registry.test/app@sha256:old"
        self.assertEqual(
            "different:remote=sha256:manifest,local=sha256:old", self.result_line()
        )

    def test_missing_remote_manifest_is_reported(self):
        self.env["MOCK_REMOTE_DIGEST"] = ""
        self.assertEqual("no-manifest", self.result_line())

    def test_missing_local_digest_is_reported(self):
        self.env["MOCK_LOCAL_REPO_DIGESTS"] = ""
        self.assertEqual("no-local-image", self.result_line())

    def test_missing_arguments_show_usage(self):
        for args in ((), ("app",)):
            with self.subTest(args=args):
                result = self.run_script(*args)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("Usage:", result.stdout)


if __name__ == "__main__":
    unittest.main()
