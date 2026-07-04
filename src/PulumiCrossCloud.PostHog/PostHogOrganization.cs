using System.Text.Json;
using Pulumi;
using Pulumi.Command.Local;

namespace PulumiCrossCloud.PostHog;

public sealed class PostHogOrganizationArgs : ResourceArgs
{
    public required Input<string> ApiKey { get; init; }
    public required Input<string> Name { get; init; }
    public Input<string> Host { get; init; } = "https://us.posthog.com";
    public bool DeleteOnDestroy { get; init; }
}

public sealed class PostHogOrganization : ComponentResource
{
    public Output<string> OrganizationId { get; }
    public Output<string> Slug { get; }
    public Output<string> RawJson { get; }

    public PostHogOrganization(
        string name,
        PostHogOrganizationArgs args,
        ComponentResourceOptions? options = null
    )
        : base("posthog:Organization", name, null, options)
    {
        ArgumentNullException.ThrowIfNull(args);

        var commandArgs = new CommandArgs
        {
            Create = CreateOrganizationScript,
            Update = CreateOrganizationScript,
            Environment =
            {
                ["POSTHOG_API_KEY"] = args.ApiKey,
                ["POSTHOG_HOST"] = args.Host,
                ["POSTHOG_ORGANIZATION_NAME"] = args.Name
            }
        };

        if (args.DeleteOnDestroy)
        {
            commandArgs.Delete = DeleteOrganizationScript;
        }

        var command = new Command(
            $"{name}-organization",
            commandArgs,
            new CustomResourceOptions { Parent = this }
        );

        RawJson = command.Stdout;
        OrganizationId = RawJson.Apply(GetJsonField("id"));
        Slug = RawJson.Apply(GetJsonField("slug"));

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["organizationId"] = OrganizationId,
            ["slug"] = Slug
        });
    }

    private static Func<string, string> GetJsonField(string fieldName) =>
        json =>
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(fieldName, out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        };

    private const string CreateOrganizationScript = """
python3 - <<'PY'
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

api_key = os.environ["POSTHOG_API_KEY"]
host = os.environ.get("POSTHOG_HOST", "https://us.posthog.com").rstrip("/")
name = os.environ["POSTHOG_ORGANIZATION_NAME"]

def request(method, path, payload=None):
    data = None
    headers = {"Authorization": f"Bearer {api_key}"}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"

    req = urllib.request.Request(f"{host}{path}", data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as response:
            body = response.read().decode("utf-8")
            return json.loads(body) if body else {}
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8")
        raise RuntimeError(f"PostHog API {method} {path} failed: {exc.code} {detail}") from exc

def find_existing():
    path = "/api/organizations/?limit=100"
    while path:
        payload = request("GET", path)
        for organization in payload.get("results", []):
            if organization.get("name") == name:
                return organization

        next_url = payload.get("next")
        if not next_url:
            return None
        path = urllib.parse.urlsplit(next_url).path
        query = urllib.parse.urlsplit(next_url).query
        if query:
            path = f"{path}?{query}"

existing = find_existing()
organization = existing if existing is not None else request("POST", "/api/organizations/", {"name": name})
print(json.dumps({
    "id": organization.get("id", ""),
    "name": organization.get("name", name),
    "slug": organization.get("slug", "")
}))
PY
""";

    private const string DeleteOrganizationScript = """
python3 - <<'PY'
import json
import os
import urllib.error
import urllib.parse
import urllib.request

api_key = os.environ["POSTHOG_API_KEY"]
host = os.environ.get("POSTHOG_HOST", "https://us.posthog.com").rstrip("/")
name = os.environ["POSTHOG_ORGANIZATION_NAME"]

def request(method, path, payload=None):
    data = None
    headers = {"Authorization": f"Bearer {api_key}"}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"

    req = urllib.request.Request(f"{host}{path}", data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as response:
            body = response.read().decode("utf-8")
            return json.loads(body) if body else {}
    except urllib.error.HTTPError as exc:
        if exc.code == 404:
            return None
        detail = exc.read().decode("utf-8")
        raise RuntimeError(f"PostHog API {method} {path} failed: {exc.code} {detail}") from exc

def find_existing():
    path = "/api/organizations/?limit=100"
    while path:
        payload = request("GET", path)
        if payload is None:
            return None

        for organization in payload.get("results", []):
            if organization.get("name") == name:
                return organization

        next_url = payload.get("next")
        if not next_url:
            return None
        path = urllib.parse.urlsplit(next_url).path
        query = urllib.parse.urlsplit(next_url).query
        if query:
            path = f"{path}?{query}"

organization = find_existing()
if organization is not None and organization.get("id"):
    request("DELETE", f"/api/organizations/{organization['id']}/")
PY
""";
}
