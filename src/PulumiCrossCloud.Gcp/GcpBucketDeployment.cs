using Pulumi;
using Pulumi.Command.Local;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Gcp;

public class GcpBucketDeployment : BucketDeploymentResource
{
    public Output<string> WebsiteUrl { get; }

    public GcpBucketDeployment(string name, string bucketName, string localPath, string? keyPrefix = null, BucketSyncOptions? options = null, ComponentResourceOptions? resourceOptions = null)
        : base("crosscloud:gcp:GcpBucketDeployment", name, localPath, options, resourceOptions)
    {
        CommandValidator.EnsureCommandExists(
            "gsutil", 
            "Google Cloud SDK", 
            "https://cloud.google.com/sdk/docs/install", 
            brewPkg: "--cask google-cloud-sdk",
            wingetPkg: "Google.CloudSDK",
            linuxPkg: "google-cloud-sdk");

        var destination = !string.IsNullOrEmpty(keyPrefix) 
            ? $"gs://{bucketName}/{keyPrefix.Trim('/')}" 
            : $"gs://{bucketName}";

        // Use Pulumi Command to run 'gsutil rsync'
        // -r: recursive, -d: delete extraneous files
        var syncCommand = new Command($"{name}-sync", new CommandArgs
        {
            Create = $"gsutil rsync -r -d {localPath} {destination}",
            Update = $"gsutil rsync -r -d {localPath} {destination}",
            Triggers = new InputList<string> { (Input<string>)ContentHash }
        }, new CustomResourceOptions 
        { 
            Parent = this,
            DependsOn = BuildResource != null ? [BuildResource] : []
        });

        WebsiteUrl = Output.Create(destination);
    }
}
