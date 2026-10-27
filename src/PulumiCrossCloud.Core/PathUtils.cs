using System.Runtime.CompilerServices;

namespace PulumiCrossCloud.Core;

public static class PathUtils
{
    /// <summary>
    /// Returns the absolute path to the repository root.
    /// It searches upwards from the caller's file path for a marker file (.git or .slnx).
    /// </summary>
    public static string GetRepoRoot([CallerFilePath] string? sourceFilePath = null)
    {
        if (string.IsNullOrEmpty(sourceFilePath))
            return Directory.GetCurrentDirectory();

        var directory = Path.GetDirectoryName(sourceFilePath);

        while (directory is not null)
        {
            // Look for common root markers
            if (
                Directory.Exists(directory)
                && (
                    Directory.Exists(Path.Combine(directory, ".git"))
                    || File.Exists(Path.Combine(directory, ".git"))
                    || Directory.EnumerateFiles(directory, "*.slnx").Any()
                    || Directory.EnumerateFiles(directory, "*.sln").Any()
                )
            )
                return directory;

            directory = Path.GetDirectoryName(directory);
        }

        // Fallback to current directory if no root marker found
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Resolves a path relative to the repository root.
    /// </summary>
    /// <param name="relativePath">The path relative to the root (e.g., "backend/Dockerfile").</param>
    /// <param name="sourceFilePath">Automatically populated caller path.</param>
    public static string ResolvePath(
        string relativePath,
        [CallerFilePath] string? sourceFilePath = null
    )
    {
        return Path.GetFullPath(Path.Combine(GetRepoRoot(sourceFilePath), relativePath));
    }

    /// <summary>
    /// Finds the latest migration ID in a directory by looking at file names.
    /// </summary>
    public static string GetLatestMigrationId(string migrationsPath)
    {
        if (!Directory.Exists(migrationsPath))
            return "none";

        var latestFile = Directory
            .GetFiles(migrationsPath, "*.cs")
            .Select(Path.GetFileName)
            .Where(f =>
                f is not null
                && f.Length > 15
                && f.Take(14).All(char.IsAsciiDigit)
                && f[14] == '_'
                && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            )
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .FirstOrDefault();

        return latestFile is not null ? Path.GetFileNameWithoutExtension(latestFile) : "none";
    }
}
