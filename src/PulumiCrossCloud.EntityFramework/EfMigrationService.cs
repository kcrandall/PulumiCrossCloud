using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.EntityFramework;

public class EfContextSettings
{
    public string ContextName { get; set; } = null!;
    public string ProjectPath { get; set; } = null!;
    public string StartupProjectPath { get; set; } = null!;
}

public class EfMigrationServiceOptions
{
    public EfContextSettings[]? Contexts { get; set; }
    public string? WorkingDirectory { get; set; }
    public IDictionary<string, string>? EnvironmentVariables { get; set; }
}

public class EfMigrationService
{
    private readonly EfMigrationServiceOptions _options;

    public EfMigrationService(EfMigrationServiceOptions options)
    {
        _options = options;
    }

    // Static flags to ensure we only run setup tasks once per Pulumi process
    private static bool _hasCheckedDotnet = false;
    private static bool _hasUpdatedEf = false;
    private static bool _hasBuilt = false;
    private static readonly object _setupLock = new object();

    public async Task RunMigrationsAsync(bool apply = true)
    {
        lock (_setupLock)
        {
            if (!_hasCheckedDotnet)
            {
                CheckDotnetVersion();
                _hasCheckedDotnet = true;
            }

            if (!_hasUpdatedEf)
            {
                CheckAndUpdateEfToolSync();
                _hasUpdatedEf = true;
            }

            if (_options.Contexts == null || _options.Contexts.Length == 0)
            {
                Log.Warn("No database contexts provided. Skipping migrations.");
                return;
            }

            if (!_hasBuilt)
            {
                var startupProject = _options.Contexts[0].StartupProjectPath;
                Log.Info($"Building {Path.GetFileName(startupProject)}...");
                // Use -v q (quiet) to suppress all build output
                RunQuietCommand("dotnet", $"build \"{startupProject}\" -v q /nologo /p:PublishAot=false");
                _hasBuilt = true;
            }
        }

        foreach (var context in _options.Contexts)
        {
            if (!apply)
            {
                var pending = await GetPendingMigrationsAsync(context);
                if (pending.Count > 0)
                {
                    Log.Info($"Context {context.ContextName} has {pending.Count} pending migrations: {string.Join(", ", pending)}");
                    await ValidateUpdatePossibleAsync(context);
                }
            }
            else
            {
                // Optional reset support: if EF_RESET env contains this context name or 'all',
                // first migrate to 0 then back to latest to rebuild from scratch.
                var resetTargets = (_options.EnvironmentVariables != null && _options.EnvironmentVariables.TryGetValue("EF_RESET", out var resetVal))
                    ? (resetVal ?? string.Empty)
                    : string.Empty;

                var shouldReset = !string.IsNullOrWhiteSpace(resetTargets)
                    && (resetTargets.Equals("all", StringComparison.OrdinalIgnoreCase)
                        || resetTargets
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Any(s => s.Equals(context.ContextName, StringComparison.OrdinalIgnoreCase)));

                if (shouldReset)
                {
                    Log.Warn($"EF_RESET matched for {context.ContextName}. Rebuilding: update 0 -> latest...");
                    await RunEfUpdateAsync(context, targetMigration: "0");
                    await RunEfUpdateAsync(context, targetMigration: null);
                }
                else
                {
                    await RunEfUpdateAsync(context, targetMigration: null);
                }
            }
        }
    }

    private void CheckDotnetVersion()
    {
        var version = RunQuietCommand("dotnet", "--version");
        Log.Debug($".NET SDK: {version.Trim()}");
    }

    private void CheckAndUpdateEfToolSync()
    {
        try
        {
            var versionOutput = RunQuietCommand("dotnet", "ef --version");
            var versionLine = versionOutput.Split('\n').FirstOrDefault(l => l.Contains("Entity Framework Core .NET Command-line Tools")) ?? "unknown";
            Log.Debug($"Found dotnet-ef: {versionLine.Trim()}");
            
            RunQuietCommand("dotnet", "tool update --global dotnet-ef");
        }
        catch
        {
            Log.Warn("dotnet-ef not found. Installing...");
            RunQuietCommand("dotnet", "tool install --global dotnet-ef");
        }
    }

    private async Task ValidateUpdatePossibleAsync(EfContextSettings context)
    {
        Log.Info($"Validating update for {context.ContextName}...");
        try
        {
            var args = $"ef migrations script --context \"{context.ContextName}\" --project \"{context.ProjectPath}\" --startup-project \"{context.StartupProjectPath}\" --no-build";
            await RunQuietCommandWithEnvAsync("dotnet", args);
            Log.Info($"Validation successful for {context.ContextName}.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Validation failed for {context.ContextName}: {ex.Message}");
        }
    }

    private async Task<List<string>> GetPendingMigrationsAsync(EfContextSettings context)
    {
        try 
        {
            var args = $"ef migrations list --context \"{context.ContextName}\" --project \"{context.ProjectPath}\" --startup-project \"{context.StartupProjectPath}\" --no-build";
            var output = await RunQuietCommandWithEnvAsync("dotnet", args);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            
            var migrationRegex = new System.Text.RegularExpressions.Regex(@"^\d{14}_");
            var migrationLines = lines.Where(l => migrationRegex.IsMatch(l)).ToList();
            var pending = migrationLines.Where(l => l.EndsWith("(Pending)")).Select(l => l.Replace("(Pending)", "").Trim()).ToList();

            if (pending.Count == 0)
            {
                Log.Info($"Context {context.ContextName} is up to date ({migrationLines.Count} applied).");
            }

            return pending;
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to check migrations for {context.ContextName}: {ex.Message}.");
            return ["Unknown (Check failed)"];
        }
    }

    private async Task RunEfUpdateAsync(EfContextSettings context, string? targetMigration)
    {
        Log.Info($"Applying migrations for {context.ContextName}{(targetMigration is null ? string.Empty : $" (target: {targetMigration})")}...");
        
        var args = new StringBuilder();
        args.Append($"ef database update ");
        if (!string.IsNullOrWhiteSpace(targetMigration))
        {
            args.Append($"{targetMigration} ");
        }
        args.Append($"--context \"{context.ContextName}\" --project \"{context.ProjectPath}\" --startup-project \"{context.StartupProjectPath}\" --no-build");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _options.WorkingDirectory ?? Directory.GetCurrentDirectory()
            }
        };

        process.StartInfo.EnvironmentVariables["IS_EF_MIGRATION"] = "true";
        if (_options.EnvironmentVariables != null)
        {
            foreach (var kvp in _options.EnvironmentVariables)
            {
                process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        process.OutputDataReceived += (sender, e) => 
        { 
            if (e.Data != null) 
            {
                var data = e.Data;
                // STRICT FILTER: Only log actual EF progress messages. Discard everything else.
                if (data.Contains("Applying migration") || data.Contains("Successfully applied"))
                {
                    Log.Info(data); 
                }
            }
        };
        process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Log.Error(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"EF update failed for {context.ContextName} (exit {process.ExitCode})");
        }
    }

    private string RunQuietCommand(string fileName, string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.StartInfo.EnvironmentVariables["IS_EF_MIGRATION"] = "true";

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new Exception($"Command failed (exit {process.ExitCode}): {error}");
        }

        return output;
    }

    private async Task<string> RunQuietCommandWithEnvAsync(string fileName, string arguments)
    {
        var outputBuffer = new StringBuilder();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _options.WorkingDirectory ?? Directory.GetCurrentDirectory()
            }
        };

        process.StartInfo.EnvironmentVariables["IS_EF_MIGRATION"] = "true";
        if (_options.EnvironmentVariables != null)
        {
            foreach (var kvp in _options.EnvironmentVariables)
            {
                process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuffer.AppendLine(e.Data); };
        process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Log.Error(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Command failed (exit {process.ExitCode})");
        }

        return outputBuffer.ToString();
    }
}
