import importlib.util
import io
import os
from pathlib import Path
import unittest
from unittest.mock import patch

path = Path(__file__).resolve().parents[2] / "src/PulumiCrossCloud.Neon/configure-neon-compute.py"
spec = importlib.util.spec_from_file_location("compute_policy", path)
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)


class ComputePolicyTests(unittest.TestCase):
    def setUp(self):
        self.environment = patch.dict(os.environ, {
            "NEON_PROJECT_ID": "project", "NEON_BRANCH_ID": "branch",
            "NEON_MIN_CU": "0.25", "NEON_MAX_CU": "0.25",
            "NEON_SUSPEND_TIMEOUT_SECONDS": "-1",
        })
        self.environment.start()
        self.addCleanup(self.environment.stop)
        self.endpoint = {
            "id": "endpoint", "branch_id": "branch", "type": "read_write",
            "autoscaling_limit_min_cu": 0.25, "autoscaling_limit_max_cu": 0.25,
            "suspend_timeout_seconds": 300,
        }

    def test_always_on_updates_the_existing_compute_and_confirms_it(self):
        confirmed = {**self.endpoint, "suspend_timeout_seconds": -1}
        with patch.object(policy, "request", side_effect=[{"endpoints": [self.endpoint]}, {}, {"endpoint": confirmed}]) as request, patch("sys.stdout", io.StringIO()):
            policy.main()
        path, body = request.call_args_list[1].args
        self.assertEqual("/projects/project/endpoints/endpoint", path)
        self.assertEqual(-1, body["endpoint"]["suspend_timeout_seconds"])
        self.assertEqual(3, request.call_count)

    def test_matching_policy_does_not_patch(self):
        self.endpoint["suspend_timeout_seconds"] = -1
        with patch.object(policy, "request", side_effect=[{"endpoints": [self.endpoint]}, {"endpoint": self.endpoint}]) as request, patch("sys.stdout", io.StringIO()):
            policy.main()
        self.assertTrue(all(len(call.args) == 1 for call in request.call_args_list))

    def test_partial_policy_preserves_compute_sizes(self):
        os.environ["NEON_MIN_CU"] = ""
        os.environ["NEON_MAX_CU"] = ""
        self.endpoint["autoscaling_limit_max_cu"] = 2
        confirmed = {**self.endpoint, "suspend_timeout_seconds": -1}
        with patch.object(policy, "request", side_effect=[{"endpoints": [self.endpoint]}, {}, {"endpoint": confirmed}]) as request, patch("sys.stdout", io.StringIO()):
            policy.main()
        self.assertEqual({"suspend_timeout_seconds": -1}, request.call_args_list[1].args[1]["endpoint"])

    def test_default_suspend_timeout_accepts_neons_normalized_value(self):
        os.environ["NEON_SUSPEND_TIMEOUT_SECONDS"] = "0"
        with patch.object(policy, "request", side_effect=[{"endpoints": [self.endpoint]}, {"endpoint": self.endpoint}]) as request, patch("sys.stdout", io.StringIO()):
            policy.main()
        self.assertEqual(2, request.call_count)

    def test_ambiguous_endpoint_fails_before_mutation(self):
        with patch.object(policy, "request", return_value={"endpoints": [self.endpoint, self.endpoint]}) as request:
            with self.assertRaisesRegex(RuntimeError, "exactly one"):
                policy.main()
        self.assertEqual(1, request.call_count)

    def test_invalid_limits_fail_before_mutation(self):
        os.environ["NEON_MIN_CU"] = "2"
        with patch.object(policy, "request", return_value={"endpoints": [self.endpoint]}) as request:
            with self.assertRaises(ValueError):
                policy.main()
        self.assertEqual(1, request.call_count)
