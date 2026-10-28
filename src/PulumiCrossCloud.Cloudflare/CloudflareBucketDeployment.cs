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

        if ((accessKey is null) != (secretKey is null))
            throw new ArgumentException("R2 access and secret keys must be provided together.");

        var environment = new InputMap<string>
        {
            ["CROSSCLOUD_SYNC_SOURCE"] = localPath,
            ["CROSSCLOUD_SYNC_DESTINATION"] = destination,
            ["CROSSCLOUD_R2_ENDPOINT"] = r2Endpoint,
            ["AWS_DEFAULT_REGION"] = "auto",
        };
        if (accessKey is not null && secretKey is not null)
        {
            environment["AWS_ACCESS_KEY_ID"] = Output.CreateSecret((Output<string>)accessKey);
            environment["AWS_SECRET_ACCESS_KEY"] = Output.CreateSecret((Output<string>)secretKey);
            // Explicit static credentials must not inherit an unrelated ambient session token.
            var token = Output.CreateSecret((Output<string>)(sessionToken ?? Output.Create("")));
            environment["AWS_SESSION_TOKEN"] = token;
            environment["AWS_SECURITY_TOKEN"] = token;
        }

        var (syncBase, interpreter) = GetSyncCommand(OperatingSystem.IsWindows());

        var syncCommand = new Command(
            $"{name}-sync",
            new CommandArgs
            {
                Create = syncBase,
                Update = syncBase,
                Interpreter = interpreter,
                Environment = environment,
                Logging = Logging.None,
                Triggers = new InputList<string> { (Input<string>)ContentHash },
            },
            new CustomResourceOptions
            {
                Parent = this,
                AdditionalSecretOutputs = ["stdout", "stderr"],
                DependsOn = BuildResource is not null ? [BuildResource] : [],
            }
        );

        WebsiteUrl = Output.Create(destination);
    }

    internal static (string Script, string[] Interpreter) GetSyncCommand(bool windows) =>
        windows
            ? (
                """
                $ErrorActionPreference = 'Stop'
                & aws.exe s3 sync $env:CROSSCLOUD_SYNC_SOURCE $env:CROSSCLOUD_SYNC_DESTINATION --endpoint-url $env:CROSSCLOUD_R2_ENDPOINT --delete --no-progress
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                """,
                ["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command"]
            )
            : (
                "aws s3 sync \"$CROSSCLOUD_SYNC_SOURCE\" \"$CROSSCLOUD_SYNC_DESTINATION\" --endpoint-url \"$CROSSCLOUD_R2_ENDPOINT\" --delete --no-progress",
                ["/bin/sh", "-c"]
            );
}
