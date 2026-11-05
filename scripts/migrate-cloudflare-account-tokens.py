#!/usr/bin/env python3
"""Move Cloudflare account tokens created by the old shell command into the native provider.

Releases up to 0.1.2-alpha.20261105 created AccountApiToken (and so CloudflareR2Credentials)
tokens with a `command:local:Command`. Upgrading would delete that command, which revokes the
token, and create a new one, which rotates every credential derived from it. Run this once per
stack before the first `pulumi up` with the new release. It rewrites the stack's state so each
existing token becomes a `crosscloud-cloudflare:index:AccountToken` that keeps its ID and
value, and nothing is created or revoked:

    python3 scripts/migrate-cloudflare-account-tokens.py --stack prod            # dry run
    python3 scripts/migrate-cloudflare-account-tokens.py --stack prod --apply

Then `pulumi preview` should show no token being created or deleted. The script saves an
encrypted backup first; `pulumi stack import --file <backup>` restores it. Token values are
never printed. Requires the Pulumi CLI, logged in to the stack's backend.
"""

import argparse
import datetime
import json
import os
import re
import shlex
import subprocess
import sys
import tempfile
import uuid

SECRET_SIG = "4dabf18193072939515e22adb298388d"
SECRET_VALUE = "1b47061264138c4ac30d75fd1eb44270"
COMMAND_TYPE = "command:local:Command"
COMPONENT_TYPE = "crosscloud:cloudflare:AccountApiToken"
TOKEN_TYPE = "crosscloud-cloudflare:index:AccountToken"
PROVIDER_TYPE = "pulumi:providers:crosscloud-cloudflare"
DOWNLOAD_URL = "github://api.github.com/kcrandall/PulumiCrossCloud"


def secret(value):
    return {SECRET_SIG: SECRET_VALUE, "plaintext": json.dumps(value)}


def reveal(value):
    """Unwraps secrets in an export made with --show-secrets."""
    if isinstance(value, dict):
        if value.get(SECRET_SIG) == SECRET_VALUE:
            if "plaintext" not in value:
                raise SystemExit("The export has encrypted secrets; it must use --show-secrets.")
            return reveal(json.loads(value["plaintext"]))
        return {key: reveal(item) for key, item in value.items()}
    if isinstance(value, list):
        return [reveal(item) for item in value]
    return value


def urn_name(urn):
    return urn.rsplit("::", 1)[1]


def legacy_token(command):
    """Reads the token and the request that created it from a legacy command's state."""
    inputs = reveal(command.get("inputs", {}))
    outputs = reveal(command.get("outputs", {}))
    try:
        response = json.loads(outputs["stdout"])
        result = response["result"]
        token_id, value = result["id"], result["value"]
    except (KeyError, TypeError, json.JSONDecodeError):
        # Never echo stdout: it holds the token.
        raise SystemExit(f"{command['urn']}: its output is not a successful token response.")
    if not response.get("success") or not token_id or not value:
        raise SystemExit(f"{command['urn']}: its output is not a successful token response.")

    environment = inputs.get("environment") or {}
    create = inputs.get("create") or ""
    words = shlex.split(create) if create else []
    payload = environment.get("CLOUDFLARE_TOKEN_PAYLOAD")
    if payload is None and "--data" in words:
        # The oldest releases inlined the payload, account and token in the command.
        payload = words[words.index("--data") + 1]
    account = environment.get("CLOUDFLARE_ACCOUNT_ID")
    match = re.search(r"/accounts/([^/\"\s]+)/tokens", create)
    if account is None and match:
        account = match.group(1)
    primary = environment.get("CLOUDFLARE_PRIMARY_TOKEN")
    if primary is None:
        header = next((w for w in words if w.startswith("Authorization: Bearer ")), None)
        primary = header[len("Authorization: Bearer "):] if header else None
    if payload is None or account is None or primary is None:
        raise SystemExit(f"{command['urn']}: could not find the token request in its inputs.")

    request = json.loads(payload)
    [policy] = request["policies"]
    return {
        "id": token_id,
        "value": value,
        "account": account,
        "primary": primary,
        "name": request["name"],
        "groups": [group["id"] for group in policy["permission_groups"]],
        "resources": policy["resources"],
    }


def migrate(deployment, plugin_version):
    resources = deployment["resources"]
    types = {resource["urn"]: resource["type"] for resource in resources}
    migrated, result = [], []
    renamed = {}
    for resource in resources:
        if not (
            resource["type"] == COMMAND_TYPE
            and types.get(resource.get("parent")) == COMPONENT_TYPE
            and urn_name(resource["urn"]).endswith("-create")
        ):
            result.append(resource)
            continue

        token = legacy_token(resource)
        prefix = resource["urn"].rsplit(f"${COMMAND_TYPE}::", 1)[0]
        base = urn_name(resource["parent"])
        provider_urn = f"{prefix}${PROVIDER_TYPE}::{base}-provider"
        provider_id = str(uuid.uuid4())
        token_urn = f"{prefix}${TOKEN_TYPE}::{base}-token"
        now = datetime.datetime.now(datetime.timezone.utc).isoformat().replace("+00:00", "Z")

        provider_inputs = {
            "__internal": {"pluginDownloadURL": DOWNLOAD_URL},
            "apiToken": secret(token["primary"]),
        }
        provider_outputs = {"apiToken": secret(token["primary"])}
        if plugin_version:
            provider_inputs["version"] = plugin_version
            provider_outputs["version"] = plugin_version
        result.append(
            {
                "urn": provider_urn,
                "custom": True,
                "id": provider_id,
                "type": PROVIDER_TYPE,
                "inputs": provider_inputs,
                "outputs": provider_outputs,
                "parent": resource["parent"],
                "propertyDependencies": {"apiToken": []},
                "created": now,
                "modified": now,
            }
        )
        inputs = {
            "accountId": token["account"],
            "name": token["name"],
            "permissionGroupIds": token["groups"],
            "resources": token["resources"],
        }
        result.append(
            {
                "urn": token_urn,
                "custom": True,
                "id": token["id"],
                "type": TOKEN_TYPE,
                "inputs": inputs,
                "outputs": {**inputs, "status": "active", "value": secret(token["value"])},
                "parent": resource["parent"],
                "provider": f"{provider_urn}::{provider_id}",
                "propertyDependencies": {key: [] for key in inputs},
                "additionalSecretOutputs": ["value"],
                "created": resource.get("created", now),
                "modified": now,
            }
        )
        renamed[resource["urn"]] = token_urn
        migrated.append((resource["urn"], token_urn, token))

    # Point anything that depended on a command at the token that replaced it.
    for resource in result:
        if "dependencies" in resource:
            resource["dependencies"] = [renamed.get(urn, urn) for urn in resource["dependencies"]]
        for key, urns in (resource.get("propertyDependencies") or {}).items():
            resource["propertyDependencies"][key] = [renamed.get(urn, urn) for urn in urns]
    deployment["resources"] = result
    return migrated


def pulumi(args, stack, cwd, **kwargs):
    command = ["pulumi", *args, "--non-interactive"]
    if stack:
        command += ["--stack", stack]
    return subprocess.run(command, cwd=cwd, check=True, **kwargs)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stack", help="Stack to migrate (default: the selected stack).")
    parser.add_argument("--cwd", help="Pulumi project directory (default: current directory).")
    parser.add_argument("--plugin-version", help="crosscloud-cloudflare plugin version to record (optional).")
    parser.add_argument("--apply", action="store_true", help="Import the migrated state. Without it, only report.")
    args = parser.parse_args()

    exported = pulumi(["stack", "export", "--show-secrets"], args.stack, args.cwd, capture_output=True, text=True)
    state = json.loads(exported.stdout)
    migrated = migrate(state["deployment"], args.plugin_version)
    if not migrated:
        print("No legacy Cloudflare account token commands found; nothing to migrate.")
        return

    for old, new, token in migrated:
        print(f"{token['id']} ({token['name']}, account {token['account']})")
        print(f"  from {old}")
        print(f"  to   {new}")
    if not args.apply:
        print("Dry run: rerun with --apply to import the migrated state.")
        return

    stamp = datetime.datetime.now().strftime("%Y%m%d%H%M%S")
    backup = os.path.abspath(f"cloudflare-token-migration-backup-{args.stack or 'stack'}-{stamp}.json")
    pulumi(["stack", "export", "--file", backup], args.stack, args.cwd)
    print(f"Saved an encrypted backup to {backup}")

    # The migrated state holds plaintext secrets until Pulumi re-encrypts them on import.
    descriptor, path = tempfile.mkstemp(suffix=".json")
    try:
        with os.fdopen(descriptor, "w") as handle:
            json.dump(state, handle)
        pulumi(["stack", "import", "--file", path], args.stack, args.cwd)
    finally:
        os.remove(path)
    print("Imported. Run `pulumi preview`: it should not create or delete any Cloudflare token.")
    print(f"To undo: pulumi stack import --file {backup}")


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        sys.exit(error.returncode)
