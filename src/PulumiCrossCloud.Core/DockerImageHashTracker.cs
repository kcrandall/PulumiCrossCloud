using Pulumi;
using Pulumi.Command.Local;
using PulumiCrossCloud.Core;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
namespace PulumiCrossCloud.Core;
public class DockerImageHashTrackerArgs
{
    public string DockerfilePath { get; set; } = "";
    public string ContextPath { get; set; } = "";
    public List<string> AdditionalFiles { get; set; } = new();
}
public class DockerImageHashTracker : ComponentResource
{
    [Output("combinedHash")]
    public Output<string> CombinedHash { get; private set; }
    
    [Output("imageTag")]
    public Output<string> ImageTag { get; private set; }
    
    [Output("shouldDeploy")]
    public Output<bool> ShouldDeploy { get; private set; }

    public DockerImageHashTracker(string name, DockerImageHashTrackerArgs args, ComponentResourceOptions? options = null)
        : base("custom:docker:hash-tracker", name, options)
    {
        var currentHash = CalculateCombinedHash(
            args.DockerfilePath, 
            args.ContextPath, 
            args.AdditionalFiles.ToArray()
        );

        var imageTag = currentHash.Substring(0, 12);
        
        // Use a Command resource to store and compare hashes
        var hashFile = Path.Combine(Path.GetTempPath(), $"pulumi-hash-{name}.txt");
        var storedHash = File.Exists(hashFile) ? File.ReadAllText(hashFile).Trim() : "";
        var shouldDeploy = currentHash != storedHash;
        
        // Create a command that updates the hash file when hash changes
        var updateHashCommand = new Command($"update-hash-{name}", new CommandArgs
        {
            Create = $"echo '{currentHash}' > '{hashFile}'",
            Update = $"echo '{currentHash}' > '{hashFile}'",
            Delete = $"rm -f '{hashFile}'",
            Triggers = new[] { currentHash }
        });
        
        CombinedHash = Output.Create(currentHash);
        ImageTag = Output.Create(imageTag);
        ShouldDeploy = Output.Create(shouldDeploy);
        
        RegisterOutputs(new Dictionary<string, object?>
        {
            ["combinedHash"] = CombinedHash,
            ["imageTag"] = ImageTag,
            ["shouldDeploy"] = ShouldDeploy
        });
    }
    private static string CalculateCombinedHash(string dockerfilePath, string contextPath, string[] additionalFiles)
    {
        using var sha256 = SHA256.Create();
        var combinedContent = new StringBuilder();
        
        // Hash Dockerfile
        if (File.Exists(dockerfilePath))
        {
            var fileHash = ComputeFileHash(dockerfilePath);
            combinedContent.Append($"DOCKERFILE:{fileHash}");
        }
        else
        {
            Console.WriteLine($"[ERROR] Dockerfile path does not exist: {dockerfilePath}");
            throw new FileNotFoundException($"Dockerfile path does not exist: {dockerfilePath}");
        }
        
        // Hash additional files
        foreach (var file in additionalFiles.Where(File.Exists).OrderBy(f => f))
        {
            var fileHash = ComputeFileHash(file);
            combinedContent.Append($"FILE:{Path.GetFileName(file)}:{fileHash}");
        }
        
        // Hash relevant files from context (selective to avoid huge hashes)
        // if (Directory.Exists(contextPath))
        // {
        //     var contextFiles = Directory.GetFiles(contextPath, "*", SearchOption.AllDirectories)
        //         .Where(f => !f.Contains("/.git/") && 
        //                    !f.Contains("/node_modules/") && 
        //                    !f.Contains("/bin/") && 
        //                    !f.Contains("/obj/") &&
        //                    !Path.GetFileName(f).StartsWith(".") &&
        //                    IsTextFile(f))
        //         .OrderBy(f => f)
        //         .Take(50) // Limit to prevent massive hashes
        //         .ToArray();
        //         
        //     foreach (var file in contextFiles)
        //     {
        //         try
        //         {
        //             var fileHash = ComputeFileHash(file);
        //             combinedContent.Append($"CONTEXT:{Path.GetRelativePath(contextPath, file)}:{fileHash}");
        //         }
        //         catch
        //         {
        //             // Skip files that can't be read
        //         }
        //     }
        // }
        
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combinedContent.ToString()));
        return Convert.ToHexString(hashBytes).ToLower();
    }
    
    private static string ComputeFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLower();
    }
    
    // private static bool IsTextFile(string filePath)
    // {
    //     var textExtensions = new[] { ".cs", ".js", ".ts", ".json", ".yaml", ".yml", ".xml", ".html", ".css", ".txt", ".md", ".dockerfile" };
    //     return textExtensions.Contains(Path.GetExtension(filePath).ToLower()) || 
    //            Path.GetFileName(filePath).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase);
    // }
}
