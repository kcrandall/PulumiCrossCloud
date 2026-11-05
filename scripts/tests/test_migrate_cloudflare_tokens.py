import importlib.util
import json
import shlex
import unittest
from pathlib import Path

path = Path(__file__).resolve().parents[1] / "migrate-cloudflare-account-tokens.py"
spec = importlib.util.spec_from_file_location("migrate_tokens", path)
migration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(migration)

PREFIX = "urn:pulumi:prod::app::crosscloud:cloudflare:CloudflareR2Credentials$crosscloud:cloudflare:AccountApiToken"
COMPONENT = "urn:pulumi:prod::app::crosscloud:cloudflare:CloudflareR2Credentials$crosscloud:cloudflare:AccountApiToken::assets"
PAYLOAD = {
    "name": "assets",
    "policies": [{"effect": "allow", "resources": {"com.cloudflare.edge.r2.bucket.a_default_b": "*"},
                  "permission_groups": [{"id": "pg_rw"}]}],
}
STDOUT = json.dumps({"success": True, "result": {"id": "tok_1", "value": "bearer-value"}})


def secret(value):
    return migration.secret(value)


def deployment(command_inputs):
    return {
        "resources": [
            {"urn": COMPONENT, "custom": False, "type": "crosscloud:cloudflare:AccountApiToken"},
            {
                "urn": f"{PREFIX}$command:local:Command::assets-create",
                "custom": True,
                "id": "cmd-1",
                "type": "command:local:Command",
                "parent": COMPONENT,
                "inputs": command_inputs,
                "outputs": {**command_inputs, "stdout": secret(STDOUT)},
            },
            {"urn": "urn:pulumi:prod::app::other:index:Thing::user", "custom": True, "type": "other:index:Thing",
             "dependencies": [f"{PREFIX}$command:local:Command::assets-create"]},
        ]
    }


class MigrationTests(unittest.TestCase):
    def check(self, state):
        migrated = migration.migrate(state, plugin_version=None)
        self.assertEqual(1, len(migrated))
        types = [r["type"] for r in state["resources"]]
        self.assertNotIn("command:local:Command", types)
        provider, token = state["resources"][1], state["resources"][2]
        self.assertEqual("pulumi:providers:crosscloud-cloudflare", provider["type"])
        self.assertEqual(f"{PREFIX}$pulumi:providers:crosscloud-cloudflare::assets-provider", provider["urn"])
        self.assertEqual("primary", json.loads(provider["inputs"]["apiToken"]["plaintext"]))
        self.assertEqual(f"{PREFIX}$crosscloud-cloudflare:index:AccountToken::assets-token", token["urn"])
        self.assertEqual("tok_1", token["id"])
        self.assertEqual(f"{provider['urn']}::{provider['id']}", token["provider"])
        self.assertEqual(
            {"accountId": "acct", "name": "assets", "permissionGroupIds": ["pg_rw"],
             "resources": {"com.cloudflare.edge.r2.bucket.a_default_b": "*"}},
            token["inputs"],
        )
        self.assertEqual("bearer-value", json.loads(token["outputs"]["value"]["plaintext"]))
        self.assertEqual([token["urn"]], state["resources"][3]["dependencies"])

    def test_inline_command_format(self):
        # 0.1.2-alpha.20260930 and earlier put the account, token and payload in the command.
        create = (
            'curl -sS --fail-with-body -X POST "https://api.cloudflare.com/client/v4/accounts/acct/tokens" '
            '-H "Authorization: Bearer primary" -H "Content-Type: application/json" '
            f"--data {shlex.quote(json.dumps(PAYLOAD))}"
        )
        self.check(deployment({"create": secret(create)}))

    def test_environment_format(self):
        self.check(deployment({
            "create": "curl ...",
            "environment": {
                "CLOUDFLARE_ACCOUNT_ID": "acct",
                "CLOUDFLARE_PRIMARY_TOKEN": secret("primary"),
                "CLOUDFLARE_TOKEN_PAYLOAD": json.dumps(PAYLOAD),
            },
        }))

    def test_unrelated_commands_are_left_alone(self):
        state = {"resources": [{"urn": "urn:pulumi:prod::app::command:local:Command::other-create",
                                "type": "command:local:Command", "custom": True}]}
        self.assertEqual([], migration.migrate(state, plugin_version=None))
        self.assertEqual("command:local:Command", state["resources"][0]["type"])

    def test_failed_token_output_stops_without_printing_it(self):
        state = deployment({"create": "curl", "environment": {}})
        state["resources"][1]["outputs"]["stdout"] = secret('{"success":false,"result":{"value":"leak"}}')
        with self.assertRaises(SystemExit) as raised:
            migration.migrate(state, plugin_version=None)
        self.assertNotIn("leak", str(raised.exception))


if __name__ == "__main__":
    unittest.main()
