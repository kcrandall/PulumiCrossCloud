using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PulumiCrossCloud.Core;

public static class CommandValidator
{
    public static void EnsureCommandExists(
        string command,
        string toolName,
        string installUrl,
        string? brewPkg = null,
        string? wingetPkg = null,
        string? linuxPkg = null
    )
    {
        if (IsCommandAvailable(command))
            return;

        var message =
            $"Required CLI tool '{toolName}' ('{command}') was not found in the system PATH.\n\n"
            + $"Please install it to proceed with the deployment:\n"
            + $"Official Installation Guide: {installUrl}\n";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && brewPkg != null)
            message += $"Homebrew (macOS): brew install {brewPkg}\n";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && wingetPkg != null)
            message += $"Winget (Windows): winget install {wingetPkg}\n";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && linuxPkg != null)
            message += $"Linux: {GetLinuxInstallCommand(linuxPkg)}\n";

        throw new InvalidOperationException(message);
    }

    private static string GetLinuxInstallCommand(string packageName)
    {
        if (IsCommandAvailable("apt-get"))
            return $"sudo apt-get install {packageName}";
        if (IsCommandAvailable("dnf"))
            return $"sudo dnf install {packageName}";
        if (IsCommandAvailable("yum"))
            return $"sudo yum install {packageName}";
        if (IsCommandAvailable("pacman"))
            return $"sudo pacman -S {packageName}";
        if (IsCommandAvailable("zypper"))
            return $"sudo zypper install {packageName}";

        return $"[Use your package manager to install {packageName}]";
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
