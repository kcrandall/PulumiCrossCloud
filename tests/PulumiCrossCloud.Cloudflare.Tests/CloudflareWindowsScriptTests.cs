using System.Diagnostics;
using System.Text.Json;

namespace PulumiCrossCloud.Cloudflare.Tests;

public sealed class CloudflareWindowsScriptTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TokenCommandsSelectAnExplicitInterpreter(bool windows)
    {
        var commands = AccountApiToken.GetCommandScripts(windows);
        Assert.Equal(windows ? "powershell.exe" : "/bin/sh", commands.Interpreter[0]);
        Assert.Equal(windows ? "-Command" : "-c", commands.Interpreter[^1]);
        if (windows)
        {
            Assert.Contains("curl.exe", commands.Create, StringComparison.Ordinal);
            Assert.Contains("UTF8Encoding", commands.Create, StringComparison.Ordinal);
            Assert.Contains("--data-binary '@-'", commands.Create, StringComparison.Ordinal);
            Assert.Contains("ConvertFrom-Json", commands.Delete, StringComparison.Ordinal);
            Assert.Contains("exit $LASTEXITCODE", commands.Create, StringComparison.Ordinal);
            Assert.Contains("exit $LASTEXITCODE", commands.Delete, StringComparison.Ordinal);
            Assert.DoesNotContain("set -eu", commands.Create, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SyncSelectsAnExplicitInterpreterAndEnvironmentArguments(bool windows)
    {
        var command = CloudflareBucketDeployment.GetSyncCommand(windows);
        Assert.Equal(windows ? "powershell.exe" : "/bin/sh", command.Interpreter[0]);
        Assert.Contains(
            windows ? "$env:CROSSCLOUD_SYNC_SOURCE" : "\"$CROSSCLOUD_SYNC_SOURCE\"",
            command.Script,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("%CROSSCLOUD_SYNC_SOURCE%", command.Script, StringComparison.Ordinal);
        if (windows)
            Assert.Contains("exit $LASTEXITCODE", command.Script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsTokenScriptsPreserveLiteralJsonAndThePreviousTokenId()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var fixture = new PowerShellFixture();
        var commands = AccountApiToken.GetCommandScripts(windows: true);
        var payload = JsonSerializer.Serialize(new { name = "quoted \"é\" $(throw 'unexpected')" });
        var environment = new Dictionary<string, string>
        {
            ["CLOUDFLARE_ACCOUNT_ID"] = "account",
            ["CLOUDFLARE_PRIMARY_TOKEN"] = "dummy-primary-token",
            ["CLOUDFLARE_TOKEN_PAYLOAD"] = payload,
            ["PULUMI_COMMAND_STDOUT"] = "{\"result\":{\"id\":\"previous-token-id\"}}",
        };
        var stub = """
            function curl.exe {
                [IO.File]::WriteAllText($env:CAPTURE_ARGS, (ConvertTo-Json -InputObject @($args) -Compress))
                [IO.File]::WriteAllText($env:CAPTURE_INPUT, (@($input) -join [Environment]::NewLine))
                $global:LASTEXITCODE = [int]$env:STUB_EXIT_CODE
                '{"success":true,"result":{"id":"new-token-id","value":"new-dummy-token"}}'
            }
            """;

        Assert.Equal(0, await fixture.RunAsync(stub + "\n" + commands.Create, environment));
        Assert.Equal(payload, await File.ReadAllTextAsync(fixture.InputPath));
        var createArgs = JsonSerializer.Deserialize<string[]>(
            await File.ReadAllTextAsync(fixture.ArgumentsPath)
        )!;
        Assert.Contains("--data-binary", createArgs);
        Assert.Contains("@-", createArgs);
        Assert.DoesNotContain(payload, createArgs);

        Assert.Equal(0, await fixture.RunAsync(stub + "\n" + commands.Delete, environment));
        Assert.Contains(
            "https://api.cloudflare.com/client/v4/accounts/account/tokens/previous-token-id",
            JsonSerializer.Deserialize<string[]>(
                await File.ReadAllTextAsync(fixture.ArgumentsPath)
            )!
        );
        environment["STUB_EXIT_CODE"] = "7";
        Assert.Equal(7, await fixture.RunAsync(stub + "\n" + commands.Delete, environment));
    }

    [Fact]
    public async Task WindowsSyncPreservesShellSyntaxInArgumentsAndPropagatesFailure()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var fixture = new PowerShellFixture();
        var command = CloudflareBucketDeployment.GetSyncCommand(windows: true);
        var environment = new Dictionary<string, string>
        {
            ["CROSSCLOUD_SYNC_SOURCE"] = "C:\\assets $(throw 'unexpected') & spaces",
            ["CROSSCLOUD_SYNC_DESTINATION"] = "s3://bucket/prefix $(throw 'unexpected')",
            ["CROSSCLOUD_R2_ENDPOINT"] = "https://account.r2.cloudflarestorage.com",
        };
        var stub = """
            function aws.exe {
                [IO.File]::WriteAllText($env:CAPTURE_ARGS, (ConvertTo-Json -InputObject @($args) -Compress))
                $global:LASTEXITCODE = [int]$env:STUB_EXIT_CODE
            }
            """;

        Assert.Equal(0, await fixture.RunAsync(stub + "\n" + command.Script, environment));
        var arguments = JsonSerializer.Deserialize<string[]>(
            await File.ReadAllTextAsync(fixture.ArgumentsPath)
        )!;
        Assert.Equal(environment["CROSSCLOUD_SYNC_SOURCE"], arguments[2]);
        Assert.Equal(environment["CROSSCLOUD_SYNC_DESTINATION"], arguments[3]);
        environment["STUB_EXIT_CODE"] = "9";
        Assert.Equal(9, await fixture.RunAsync(stub + "\n" + command.Script, environment));
    }

    private sealed class PowerShellFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"crosscloud-windows-shell-{Guid.NewGuid():N}"
        );
        public string ArgumentsPath => Path.Combine(_directory, "arguments.json");
        public string InputPath => Path.Combine(_directory, "input.json");

        public PowerShellFixture() => Directory.CreateDirectory(_directory);

        public async Task<int> RunAsync(
            string script,
            IReadOnlyDictionary<string, string> environment
        )
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    ArgumentList =
                    {
                        "-NoLogo",
                        "-NoProfile",
                        "-NonInteractive",
                        "-Command",
                        script,
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            process.StartInfo.Environment["STUB_EXIT_CODE"] = "0";
            foreach (var pair in environment)
                process.StartInfo.Environment[pair.Key] = pair.Value;
            process.StartInfo.Environment["CAPTURE_ARGS"] = ArgumentsPath;
            process.StartInfo.Environment["CAPTURE_INPUT"] = InputPath;
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await stdout;
            var error = await stderr;
            Assert.True(process.ExitCode is 0 or 7 or 9, error);
            return process.ExitCode;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
