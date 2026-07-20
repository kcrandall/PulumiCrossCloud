namespace PulumiCrossCloud.Core;

public enum BucketPermissions
{
    ReadObject,
    WriteObject,
    DeleteObject,
    ListBucket,
    GetBucketMetadata,
    ManageBucket,
}

public enum SecretPermissions
{
    Read,
    Write,
    Delete,
    List,
    GetMetadata,
}

public enum ContainerRegistryPermissions
{
    Pull,
    Push,
    Delete,
    List,
    GetMetadata,
    ManageRepository,
    GetAuthToken,
}

public enum ComputePermissions
{
    Start,
    Stop,
    Restart,
    Terminate,
    GetStatus,
    GetLogs,
    Connect,
    Scale,
    UpdateConfiguration,
}

public enum ComputeServiceType
{
    // AWS
    Fargate,
    EC2,
    ECS,

    // Azure
    ContainerApps,
    ContainerInstances,
    AppService,

    // GCP
    CloudRun,
    CloudRunJobs,
    ComputeEngine,

    // PaaS
    KoyebService,
    FlyMachine,
    DigitalOceanAppPlatform,
}

public enum EmailPermissions
{
    Send,
    Manage,
}
