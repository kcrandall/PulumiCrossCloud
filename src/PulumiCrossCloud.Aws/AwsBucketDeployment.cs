using Pulumi;
using Pulumi.Command.Local;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsBucketDeployment : BucketDeploymentResource
{
    public Output<string> WebsiteUrl { get; }

    public AwsBucketDeployment(string name, string bucketName, string localPath, string? keyPrefix = null, BucketSyncOptions? options = null, ComponentResourceOptions? resourceOptions = null)
        : base("crosscloud:aws:AwsBucketDeployment", name, localPath, options, resourceOptions)
    {
        CommandValidator.EnsureCommandExists(
            "aws", 
            "AWS CLI", 
            "https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html", 
            brewPkg: "awscli",
            wingetPkg: "Amazon.AWSCLI",
            linuxPkg: "awscli");

        var destination = !string.IsNullOrEmpty(keyPrefix) 
            ? $"s3://{bucketName}/{keyPrefix.Trim('/')}" 
            : $"s3://{bucketName}";

        // Use Pulumi Command to run 'aws s3 sync'
        // This is efficient and only uploads changed files.
        // We use the ContentHash as a trigger to ensure it runs when files change.
        var syncCommand = new Command($"{name}-sync", new CommandArgs
        {
            Create = $"aws s3 sync {localPath} {destination} --delete",
            Update = $"aws s3 sync {localPath} {destination} --delete",
            Triggers = new InputList<string> { (Input<string>)ContentHash }
        }, new CustomResourceOptions 
        { 
            Parent = this,
            DependsOn = BuildResource != null ? [BuildResource] : []
        });

        WebsiteUrl = Output.Create(destination);
    }
}
