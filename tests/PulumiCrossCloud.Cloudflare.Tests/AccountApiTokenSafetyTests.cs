using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Pulumi;
using Pulumi.Command.Local;
using Pulumi.Testing;

namespace PulumiCrossCloud.Cloudflare.Tests;

public sealed class AccountApiTokenSafetyTests
{
    private const string PrimaryToken = "test-primary-bearer";
    private const string Response =
        "{\"success\":true,\"result\":{\"id\":\"previous-token-id\",\"value\":\"created-bearer-token\"}}";

    [Fact]
    public async Task TokenCommandProtectsCredentialsAndReturnedToken()
    {
        var recording = await RecordTokenAsync("protected-token");
        var create = Assert.IsType<string>(recording.Command.Inputs["create"]);
        var environment = Environment(recording.Command);
        var options = Assert.IsType<CustomResourceOptions>(recording.Options);

        Assert.DoesNotContain(PrimaryToken, create, StringComparison.Ordinal);
        Assert.Equal(PrimaryToken, environment["CLOUDFLARE_PRIMARY_TOKEN"]);
        Assert.Equal("none", recording.Command.Inputs["logging"]);
        Assert.Equal(
            AccountApiToken.GetCommandScripts(OperatingSystem.IsWindows()).Interpreter,
            Assert
                .IsAssignableFrom<IEnumerable<object>>(recording.Command.Inputs["interpreter"])
                .Cast<string>()
                .ToArray()
        );
        Assert.True(Assert.IsType<bool>(recording.Command.Inputs["addPreviousOutputInEnv"]));
        Assert.Contains("stdout", options.AdditionalSecretOutputs);
        Assert.Contains("stderr", options.AdditionalSecretOutputs);
        Assert.True(await Output.IsSecretAsync(recording.Token.TokenValue));
        Assert.True(await Output.IsSecretAsync(recording.Credentials.SecretAccessKey));
    }

    [Fact]
    public async Task TokenNameContainingShellSyntaxIsPassedAsLiteralJson()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var shell = new FakeShell();
        var marker = Path.Combine(shell.DirectoryPath, "unexpected-command");
        var name = $"token-$(touch {marker})";
        var recording = await RecordTokenAsync(name);
        var environment = Environment(recording.Command);
        var create = Assert.IsType<string>(recording.Command.Inputs["create"]);

        await shell.RunAsync(create, environment);

        Assert.False(File.Exists(marker));
        var curlArguments = await File.ReadAllLinesAsync(shell.CurlCapture);
        var payloadIndex = Array.IndexOf(curlArguments, "--data") + 1;
        Assert.True(payloadIndex > 0);
        using var document = JsonDocument.Parse(curlArguments[payloadIndex]);
        Assert.Equal(name, document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task DeleteUsesThePreviousTokenIdAndStopsIfItCannotBeParsed()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var shell = new FakeShell();
        var recording = await RecordTokenAsync("shared-token-name");
        var environment = Environment(recording.Command)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        environment["PULUMI_COMMAND_STDOUT"] = Response;
        var delete = Assert.IsType<string>(recording.Command.Inputs["delete"]);

        await shell.RunAsync(delete, environment);

        Assert.Equal(Response, await File.ReadAllTextAsync(shell.JqCapture));
        Assert.Contains(
            "https://api.cloudflare.com/client/v4/accounts/test-account/tokens/previous-token-id",
            await File.ReadAllLinesAsync(shell.CurlCapture)
        );
        File.Delete(shell.CurlCapture);
        environment["JQ_FAIL"] = "true";
        Assert.NotEqual(0, await shell.RunAsync(delete, environment, expectSuccess: false));
        Assert.False(File.Exists(shell.CurlCapture));
    }

    private static async Task<TokenRecording> RecordTokenAsync(string name)
    {
        var mocks = new TokenMocks();
        AccountApiToken? token = null;
        CloudflareR2Credentials? credentials = null;
        ResourceOptions? options = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                token = new AccountApiToken(
                    name,
                    "test-account",
                    PrimaryToken,
                    ["permission-group"],
                    new ComponentResourceOptions
                    {
                        ResourceTransformations =
                        [
                            args =>
                            {
                                if (args.Resource.GetResourceType() == "command:local:Command")
                                    options = args.Options;
                                return null;
                            },
                        ],
                    }
                );
                credentials = new CloudflareR2Credentials(
                    "derived-creds",
                    "test-account",
                    "bucket",
                    PrimaryToken
                );
            }
        );

        return new TokenRecording(
            token!,
            credentials!,
            mocks.Resources.Single(resource => resource.Name == $"{name}-create"),
            options!
        );
    }

    private static ImmutableDictionary<string, object> Environment(MockResourceArgs command) =>
        Assert.IsType<ImmutableDictionary<string, object>>(command.Inputs["environment"]);

    private sealed record TokenRecording(
        AccountApiToken Token,
        CloudflareR2Credentials Credentials,
        MockResourceArgs Command,
        ResourceOptions Options
    );

    private sealed class TokenMocks : IMocks
    {
        public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            Resources.Enqueue(args);
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (args.Type == "command:local:Command")
            {
                state["stdout"] = Response;
                state["stderr"] = "";
            }
            return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(new Dictionary<string, object>());
    }

    private sealed class FakeShell : IDisposable
    {
        public string DirectoryPath { get; } =
            Path.Combine(Path.GetTempPath(), $"crosscloud-token-tests-{Guid.NewGuid():N}");
        public string CurlCapture => Path.Combine(DirectoryPath, "curl-arguments");
        public string JqCapture => Path.Combine(DirectoryPath, "jq-input");

        public FakeShell()
        {
            Directory.CreateDirectory(DirectoryPath);
            if (!OperatingSystem.IsWindows())
            {
                WriteExecutable(
                    "curl",
                    "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$CURL_CAPTURE\"\nprintf '%s' '"
                        + Response
                        + "'\n"
                );
                WriteExecutable(
                    "jq",
                    "#!/bin/sh\ncat > \"$JQ_CAPTURE\"\n[ \"${JQ_FAIL:-false}\" = true ] && exit 5\nprintf '%s' previous-token-id\n"
                );
            }
        }

        public async Task<int> RunAsync(
            string command,
            IReadOnlyDictionary<string, object> environment,
            bool expectSuccess = true
        )
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    ArgumentList = { "-c", command },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            foreach (var pair in environment)
                process.StartInfo.Environment[pair.Key] = Assert.IsType<string>(pair.Value);
            process.StartInfo.Environment["PATH"] =
                DirectoryPath + Path.PathSeparator + process.StartInfo.Environment["PATH"];
            process.StartInfo.Environment["CURL_CAPTURE"] = CurlCapture;
            process.StartInfo.Environment["JQ_CAPTURE"] = JqCapture;
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await stdout;
            var error = await stderr;
            if (expectSuccess)
                Assert.True(process.ExitCode == 0, error);
            return process.ExitCode;
        }

        private void WriteExecutable(string name, string content)
        {
            var path = Path.Combine(DirectoryPath, name);
            File.WriteAllText(path, content);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
