using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public enum AwsPostgresType
{
    Rds,
    Aurora,
}

public abstract class AwsPostgresOptions : PostgresOptions
{
    public abstract AwsPostgresType Type { get; }

    /// <summary>
    /// The VPC ID where the database should be deployed.
    /// </summary>
    public string? VpcId { get; set; }

    /// <summary>
    /// List of subnet IDs for the database subnet group.
    /// </summary>
    public string[]? SubnetIds { get; set; }

    /// <summary>
    /// List of security group IDs to associate with the database.
    /// </summary>
    public string[]? SecurityGroupIds { get; set; }
}

public class AwsRdsPostgresOptions : AwsPostgresOptions
{
    public override AwsPostgresType Type => AwsPostgresType.Rds;

    public string InstanceClass { get; set; } = "db.t3.micro";
    public int AllocatedStorage { get; set; } = 20;
    public string StorageType { get; set; } = "gp3";
}

public class AwsAuroraPostgresOptions : AwsPostgresOptions
{
    public override AwsPostgresType Type => AwsPostgresType.Aurora;

    /// <summary>
    /// If true, the cluster will support Serverless v2 scaling.
    /// </summary>
    public bool IsServerless { get; set; } = true;

    /// <summary>
    /// Minimum ACUs (Aurora Capacity Units). Only applicable for instances using ServerlessV2.
    /// </summary>
    public double MinCapacity { get; set; } = 0.5;

    /// <summary>
    /// Maximum ACUs (Aurora Capacity Units). Only applicable for instances using ServerlessV2.
    /// </summary>
    public double MaxCapacity { get; set; } = 1.0;

    /// <summary>
    /// Instance class for the primary (writer) instance.
    /// Use "db.serverless" for Serverless v2.
    /// </summary>
    public string InstanceClass { get; set; } = "db.serverless";

    /// <summary>
    /// Optional configurations for read replicas.
    /// </summary>
    public List<AwsAuroraInstanceOptions> Readers { get; set; } = new();
}

public class AwsAuroraInstanceOptions
{
    /// <summary>
    /// Instance class for this specific replica.
    /// Defaults to the cluster's primary instance class if null.
    /// </summary>
    public string? InstanceClass { get; set; }

    /// <summary>
    /// Promotion tier (0-15). Lower numbers have higher priority.
    /// </summary>
    public int PromotionTier { get; set; } = 1;
}
