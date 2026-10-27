using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Core.Tests;

public sealed class PathUtilsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"crosscloud-path-tests-{Guid.NewGuid():N}"
    );

    public PathUtilsTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void GetRepoRoot_ResolvesEachCallerIndependently()
    {
        var first = CreateRepository("first");
        var second = CreateRepository("second");

        Assert.Equal(first, PathUtils.GetRepoRoot(Path.Combine(first, "src", "Program.cs")));
        Assert.Equal(second, PathUtils.GetRepoRoot(Path.Combine(second, "src", "Program.cs")));
    }

    [Fact]
    public void GetRepoRoot_AcceptsGitWorktreeMarkerFiles()
    {
        var root = Path.Combine(_directory, "worktree");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".git"), "gitdir: /some/repo/.git/worktrees/test");

        Assert.Equal(root, PathUtils.GetRepoRoot(Path.Combine(root, "src", "Program.cs")));
    }

    [Fact]
    public void GetRepoRoot_MissingCompiledCallerPathFallsBackToCurrentDirectory()
    {
        var missing = Path.Combine(_directory, "missing", "src", "Program.cs");

        Assert.Equal(Directory.GetCurrentDirectory(), PathUtils.GetRepoRoot(missing));
    }

    [Fact]
    public void GetLatestMigrationId_IgnoresDesignerFilesAndNonMigrationSources()
    {
        File.WriteAllText(Path.Combine(_directory, "20260901120000_First.cs"), "");
        File.WriteAllText(Path.Combine(_directory, "20261027150000_Second.cs"), "");
        File.WriteAllText(Path.Combine(_directory, "20261027150000_Second.Designer.cs"), "");
        File.WriteAllText(Path.Combine(_directory, "9999_Unrelated.cs"), "");
        File.WriteAllText(Path.Combine(_directory, "AppDbContextModelSnapshot.cs"), "");

        Assert.Equal("20261027150000_Second", PathUtils.GetLatestMigrationId(_directory));
    }

    [Fact]
    public void GetLatestMigrationId_NoMigrationFilesReturnsNone()
    {
        File.WriteAllText(Path.Combine(_directory, "AppDbContextModelSnapshot.cs"), "");

        Assert.Equal("none", PathUtils.GetLatestMigrationId(_directory));
    }

    private string CreateRepository(string name)
    {
        var root = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        return root;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
