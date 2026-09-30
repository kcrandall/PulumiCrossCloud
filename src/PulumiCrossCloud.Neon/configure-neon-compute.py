"""Apply the policy to the existing default Neon endpoint; never create/delete computes."""

import json
import os
import time
import urllib.error
import urllib.request


def request(path, body=None):
    payload = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(
        "https://console.neon.tech/api/v2" + path,
        data=payload,
        method="GET" if body is None else "PATCH",
        headers={
            "Authorization": "Bearer " + os.environ["NEON_API_KEY"],
            "Content-Type": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"Neon compute policy request failed: HTTP {error.code}") from None


def main():
    project = os.environ["NEON_PROJECT_ID"]
    branch = os.environ["NEON_BRANCH_ID"]
    desired = {}
    for key, variable, convert in (
        ("autoscaling_limit_min_cu", "NEON_MIN_CU", float),
        ("autoscaling_limit_max_cu", "NEON_MAX_CU", float),
        ("suspend_timeout_seconds", "NEON_SUSPEND_TIMEOUT_SECONDS", int),
    ):
        if os.environ.get(variable):
            desired[key] = convert(os.environ[variable])

    # Neon resolves its default (0) to five minutes in endpoint responses.
    if desired.get("suspend_timeout_seconds") == 0:
        desired["suspend_timeout_seconds"] = 300

    endpoints = request(f"/projects/{project}/endpoints")["endpoints"]
    matches = [e for e in endpoints if e["branch_id"] == branch and e["type"] == "read_write"]
    if len(matches) != 1:
        raise RuntimeError("Expected exactly one default read/write Neon compute")
    endpoint = matches[0]
    merged = {**endpoint, **desired}
    if not 0.25 <= merged["autoscaling_limit_min_cu"] <= merged["autoscaling_limit_max_cu"]:
        raise ValueError("Neon compute limits must satisfy 0.25 <= minCu <= maxCu")
    if not -1 <= merged["suspend_timeout_seconds"] <= 604800:
        raise ValueError("Invalid Neon suspend timeout")
    path = f"/projects/{project}/endpoints/{endpoint['id']}"
    if any(endpoint.get(key) != value for key, value in desired.items()):
        request(path, {"endpoint": desired})

    for _ in range(30):
        endpoint = request(path)["endpoint"]
        if all(endpoint.get(key) == value for key, value in desired.items()):
            print(json.dumps({"endpointId": endpoint["id"], **desired}, sort_keys=True))
            return
        time.sleep(2)
    raise RuntimeError("Neon did not confirm the requested compute policy")


if __name__ == "__main__":
    main()
