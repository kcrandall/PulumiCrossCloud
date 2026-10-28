using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using Pulumi;
using Pulumi.Testing;

namespace PulumiCrossCloud.Cloudflare.Tests;

public sealed class CloudflareBucketDeploymentSafetyTests
{
    [Fact]
    public async Task SyncKeepsCredentialsOutOfShellSourceAndPreservesLiteralArguments()
    {
        using var fixture = new SyncFixture();
        var mocks = new SyncMocks();
        const string accessKey = "access-$(touch injected-key)";
        const string secretKey = "secret with spaces & shell syntax";
        const string keyPrefix = "prefix with spaces/$(touch injected-destination)";
        CustomResourceOptions? options = null;
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                _ = new CloudflareBucketDeployment(
                    "assets",
                    "test-bucket",
                    "test-account",
                    fixture.SourcePath,
                    keyPrefix,
                    resourceOptions: new ComponentResourceOptions
                    {
                        ResourceTransformations =
                        [
                            args =>
                            {
                                if (args.Resource.GetResourceType() == "command:local:Command")
                                    options = Assert.IsType<CustomResourceOptions>(args.Options);
                                return null;
                            },
                        ],
                    },
                    accessKey: accessKey,
                    secretKey: secretKey
                );
            }
        );

        var sync = mocks.Resources.Single(resource => resource.Name == "assets-sync");
        var command = Assert.IsType<string>(sync.Inputs["create"]);
        var environment = Assert.IsType<ImmutableDictionary<string, object>>(
            sync.Inputs["environment"]
        );
        Assert.DoesNotContain(accessKey, command, StringComparison.Ordinal);
        Assert.DoesNotContain(secretKey, command, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.SourcePath, command, StringComparison.Ordinal);
        Assert.Equal(accessKey, environment["AWS_ACCESS_KEY_ID"]);
        Assert.Equal(secretKey, environment["AWS_SECRET_ACCESS_KEY"]);
        Assert.Equal("", environment["AWS_SESSION_TOKEN"]);
        Assert.Equal("", environment["AWS_SECURITY_TOKEN"]);
        Assert.Equal("none", sync.Inputs["logging"]);
        Assert.Equal(
            CloudflareBucketDeployment.GetSyncCommand(OperatingSystem.IsWindows()).Interpreter,
            Assert
                .IsAssignableFrom<IEnumerable<object>>(sync.Inputs["interpreter"])
                .Cast<string>()
                .ToArray()
        );
        Assert.NotNull(options);
        Assert.Contains("stdout", options.AdditionalSecretOutputs);
        Assert.Contains("stderr", options.AdditionalSecretOutputs);

        if (OperatingSystem.IsWindows())
            return;

        await fixture.RunAsync(command, environment);
        var arguments = await File.ReadAllLinesAsync(fixture.ArgumentsPath);
        Assert.Equal(
            [
                "s3",
                "sync",
                fixture.SourcePath,
                $"s3://test-bucket/{keyPrefix}",
                "--endpoint-url",
                "https://test-account.r2.cloudflarestorage.com",
                "--delete",
                "--no-progress",
            ],
            arguments
        );
        Assert.Equal(
            [accessKey, secretKey, "", ""],
            await File.ReadAllLinesAsync(fixture.CredentialsPath)
        );
        Assert.False(File.Exists(Path.Combine(fixture.DirectoryPath, "injected")));
        Assert.False(File.Exists(Path.Combine(fixture.DirectoryPath, "injected-key")));
        Assert.False(File.Exists(Path.Combine(fixture.DirectoryPath, "injected-destination")));
    }

    [Fact]
    public async Task SyncRejectsAnExplicitAccessKeyWithoutItsSecretKey()
    {
        using var fixture = new SyncFixture();
        await Deployment.TestAsync(
            new SyncMocks(),
            new TestOptions { IsPreview = false },
            () =>
            {
                Assert.Throws<ArgumentException>(() =>
                    new CloudflareBucketDeployment(
                        "partial-credentials",
                        "test-bucket",
                        "test-account",
                        fixture.SourcePath,
                        accessKey: "only-access-key"
                    )
                );
            }
        );
    }

    private sealed class SyncMocks : IMocks
    {
        public ConcurrentQueue<MockResourceArgs> Resources { get; } = new();

        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            Resources.Enqueue(args);
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            state["stdout"] = "";
            state["stderr"] = "";
            return Task.FromResult<(string? id, object state)>(($"{args.Name}-id", state));
        }

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(new Dictionary<string, object>());
    }

    private sealed class SyncFixture : IDisposable
    {
        private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");
        public string DirectoryPath { get; } =
            Path.Combine(Path.GetTempPath(), $"crosscloud-r2-tests-{Guid.NewGuid():N}");
        public string SourcePath => Path.Combine(DirectoryPath, "assets $(touch injected) files");
        public string ArgumentsPath => Path.Combine(DirectoryPath, "aws-arguments");
        public string CredentialsPath => Path.Combine(DirectoryPath, "aws-credentials");

        public SyncFixture()
        {
            Directory.CreateDirectory(SourcePath);
            File.WriteAllText(Path.Combine(SourcePath, "index.html"), "content");
            var executable = Path.Combine(
                DirectoryPath,
                OperatingSystem.IsWindows() ? "aws.cmd" : "aws"
            );
            File.WriteAllText(
                executable,
                OperatingSystem.IsWindows()
                    ? "@exit /b 0\r\n"
                    : "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$AWS_ARGUMENTS\"\nprintf '%s\\n' \"$AWS_ACCESS_KEY_ID\" \"$AWS_SECRET_ACCESS_KEY\" \"$AWS_SESSION_TOKEN\" \"$AWS_SECURITY_TOKEN\" > \"$AWS_CREDENTIALS\"\n"
            );
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    executable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
            Environment.SetEnvironmentVariable(
                "PATH",
                DirectoryPath + Path.PathSeparator + _originalPath
            );
        }

        public async Task RunAsync(string command, IReadOnlyDictionary<string, object> environment)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    ArgumentList = { "-c", command },
                    WorkingDirectory = DirectoryPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            process.StartInfo.Environment["AWS_SESSION_TOKEN"] = "unrelated-ambient-session";
            process.StartInfo.Environment["AWS_SECURITY_TOKEN"] = "legacy-ambient-session";
            foreach (var pair in environment)
                process.StartInfo.Environment[pair.Key] = Assert.IsType<string>(pair.Value);
            process.StartInfo.Environment["AWS_ARGUMENTS"] = ArgumentsPath;
            process.StartInfo.Environment["AWS_CREDENTIALS"] = CredentialsPath;
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await stdout;
            Assert.True(process.ExitCode == 0, await stderr);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", _originalPath);
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
