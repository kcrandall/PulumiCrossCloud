using Pulumi;
using Pulumi.Command.Local;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Azure;

public class AzureBucketDeployment : BucketDeploymentResource
{
    public Output<string> WebsiteUrl { get; }

    public AzureBucketDeployment(
        string name,
        Input<string> storageAccountName,
        string containerName,
        string localPath,
        string? keyPrefix = null,
        BucketSyncOptions? options = null,
        ComponentResourceOptions? resourceOptions = null
    )
        : base("crosscloud:azure:AzureBucketDeployment", name, localPath, options, resourceOptions)
    {
        CommandValidator.EnsureCommandExists(
            "az",
            "Azure CLI",
            "https://learn.microsoft.com/en-us/cli/azure/install-azure-cli",
            brewPkg: "azure-cli",
            wingetPkg: "Microsoft.AzureCLI",
            linuxPkg: "azure-cli"
        );

        // Use Azure CLI to upload blobs.
        // az storage blob upload-batch -d <container> -s <source> --account-name <account>
        var prefixArg = !string.IsNullOrEmpty(keyPrefix)
            ? $"--destination-path {keyPrefix.Trim('/')}"
            : "";

        var syncCommand = new Command(
            $"{name}-sync",
            new CommandArgs
            {
                Create = Output.Format(
                    $"az storage blob upload-batch -d {containerName} -s {localPath} --account-name {storageAccountName} {prefixArg} --overwrite"
                ),
                Update = Output.Format(
                    $"az storage blob upload-batch -d {containerName} -s {localPath} --account-name {storageAccountName} {prefixArg} --overwrite"
                ),
                Triggers = new InputList<string> { (Input<string>)ContentHash },
            },
            new CustomResourceOptions
            {
                Parent = this,
                DependsOn = BuildResource != null ? [BuildResource] : [],
            }
        );

        WebsiteUrl = Output.Format(
            $"https://{storageAccountName}.blob.core.windows.net/{containerName}"
        );
    }
}
