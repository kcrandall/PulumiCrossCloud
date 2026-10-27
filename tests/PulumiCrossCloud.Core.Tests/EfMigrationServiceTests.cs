using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.EntityFramework;

namespace PulumiCrossCloud.Core.Tests;

[Collection("Pulumi deployments")]
public sealed class EfMigrationServiceTests
{
    [Fact]
    public async Task EveryDistinctStartupProjectIsBuiltForEachService()
    {
        await Deployment.TestAsync(
            new EmptyMocks(),
            new TestOptions { IsPreview = false },
            async () =>
            {
                var options = new EfMigrationServiceOptions
                {
                    WorkingDirectory = Path.GetTempPath(),
                    Contexts =
                    [
                        Context("First", "first.csproj"),
                        Context("Second", "second.csproj"),
                        Context("Shared", "first.csproj"),
                    ],
                };
                var first = new RecordingMigrationService(options);
                var second = new RecordingMigrationService(options);

                await first.RunMigrationsAsync(apply: false);
                await first.RunMigrationsAsync(apply: false);
                await second.RunMigrationsAsync(apply: false);

                RecordingMigrationService[] services = [first, second];
                foreach (var service in services)
                {
                    var builds = service
                        .Commands.Where(command => command.StartsWith("build "))
                        .ToArray();
                    Assert.Equal(2, builds.Length);
                    Assert.Contains(builds, command => command.Contains("first.csproj"));
                    Assert.Contains(builds, command => command.Contains("second.csproj"));
                }
            }
        );
    }

    [Fact]
    public async Task QuietCommandDrainsLargeStderrWithoutBlocking()
    {
        var service = new ProcessMigrationService(new EfMigrationServiceOptions());
        var fileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh";
        var arguments = OperatingSystem.IsWindows()
            ? "-NoProfile -Command \"[Console]::Error.Write(('e' * 262144)); [Console]::Out.Write('completed')\""
            : "-c \"head -c 262144 /dev/zero >&2; printf completed\"";

        var output = await Task.Run(() => service.Run(fileName, arguments))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("completed", output);
    }

    private static EfContextSettings Context(string name, string startupProject) =>
        new()
        {
            ContextName = name,
            ProjectPath = startupProject,
            StartupProjectPath = startupProject,
        };

    private sealed class RecordingMigrationService(EfMigrationServiceOptions options)
        : EfMigrationService(options)
    {
        public List<string> Commands { get; } = [];

        protected override string RunQuietCommand(string fileName, string arguments)
        {
            Commands.Add(arguments);
            return "";
        }

        protected override Task<string> RunQuietCommandWithEnvAsync(
            string fileName,
            string arguments
        ) => Task.FromResult("");
    }

    private sealed class ProcessMigrationService(EfMigrationServiceOptions options)
        : EfMigrationService(options)
    {
        public string Run(string fileName, string arguments) =>
            RunQuietCommand(fileName, arguments);
    }

    private sealed class EmptyMocks : IMocks
    {
        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args) =>
            Task.FromResult<(string? id, object state)>((args.Name, args.Inputs));

        public Task<object> CallAsync(MockCallArgs args) =>
            Task.FromResult<object>(new Dictionary<string, object>());
    }
}
