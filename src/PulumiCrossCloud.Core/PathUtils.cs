using System.Runtime.CompilerServices;

namespace PulumiCrossCloud.Core;

public static class PathUtils
{
    private static string? _cachedRepoRoot;

    /// <summary>
    /// Returns the absolute path to the repository root.
    /// It searches upwards from the caller's file path for a marker file (.git or .slnx).
    /// </summary>
    public static string GetRepoRoot([CallerFilePath] string? sourceFilePath = null)
    {
        if (_cachedRepoRoot != null)
            return _cachedRepoRoot;

        if (string.IsNullOrEmpty(sourceFilePath))
            return Directory.GetCurrentDirectory();

        var directory = Path.GetDirectoryName(sourceFilePath);

        while (directory != null)
        {
            // Look for common root markers
            if (
                Directory.Exists(Path.Combine(directory, ".git"))
                || Directory.GetFiles(directory, "*.slnx").Any()
                || Directory.GetFiles(directory, "*.sln").Any()
            )
            {
                _cachedRepoRoot = directory;
                return directory;
            }

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
            .Where(f => f != null && char.IsDigit(f[0])) // Starts with timestamp
            .OrderByDescending(f => f)
            .FirstOrDefault();

        return latestFile != null ? Path.GetFileNameWithoutExtension(latestFile) : "none";
    }
}
