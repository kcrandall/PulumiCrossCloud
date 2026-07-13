using Pulumi;
using Pulumi.Command.Local;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Cloudflare;

public class CloudflareBucketDeployment : BucketDeploymentResource
{
    public Output<string> WebsiteUrl { get; }

    public CloudflareBucketDeployment(
        string name,
        string bucketName,
        string accountId,
        string localPath,
        string? keyPrefix = null,
        BucketSyncOptions? options = null,
        ComponentResourceOptions? resourceOptions = null,
        Input<string>? accessKey = null,
        Input<string>? secretKey = null,
        Input<string>? sessionToken = null
    )
        : base(
            "crosscloud:cloudflare:CloudflareBucketDeployment",
            name,
            localPath,
            options,
            resourceOptions
        )
    {
        CommandValidator.EnsureCommandExists(
            "aws",
            "AWS CLI (required for R2 sync)",
            "https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html",
            brewPkg: "awscli",
            wingetPkg: "Amazon.AWSCLI",
            linuxPkg: "awscli"
        );

        var destination = !string.IsNullOrEmpty(keyPrefix)
            ? $"s3://{bucketName}/{keyPrefix.Trim('/')}"
            : $"s3://{bucketName}";

        var r2Endpoint = $"https://{accountId}.r2.cloudflarestorage.com";

        // We pass the credentials directly in the command string to be 100% sure they are picked up.
        // This also prevents "Partial credentials found" errors if some variables are in the host env.
        var syncBase = Output
            .Tuple(
                accessKey ?? Output.Create(""),
                secretKey ?? Output.Create(""),
                sessionToken ?? Output.Create("")
            )
            .Apply(t =>
            {
                var ak = t.Item1;
                var sk = t.Item2;
                var st = t.Item3;
                var auth = "";
                if (!string.IsNullOrEmpty(ak) && !string.IsNullOrEmpty(sk))
                {
                    auth =
                        $"AWS_ACCESS_KEY_ID={ak} AWS_SECRET_ACCESS_KEY={sk} AWS_DEFAULT_REGION=auto ";
                    if (!string.IsNullOrEmpty(st))
                        auth += $"AWS_SESSION_TOKEN={st} ";
                }
                return $"{auth}aws s3 sync {localPath} {destination} --endpoint-url {r2Endpoint} --delete --no-progress";
            });

        var syncCommand = new Command(
            $"{name}-sync",
            new CommandArgs
            {
                Create = syncBase,
                Update = syncBase,
                Triggers = new InputList<string> { (Input<string>)ContentHash },
            },
            new CustomResourceOptions
            {
                Parent = this,
                DependsOn = BuildResource != null ? [BuildResource] : [],
            }
        );

        WebsiteUrl = Output.Create(destination);
    }
}
