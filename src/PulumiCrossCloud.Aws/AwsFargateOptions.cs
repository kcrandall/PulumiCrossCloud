using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

/// <summary>
/// Selects the AWS container orchestrator used to run a Fargate workload.
/// </summary>
public enum AwsFargateBackend
{
    /// <summary>
    /// Run the workload as an Amazon ECS service. This is the default backend.
    /// </summary>
    Ecs,

    /// <summary>
    /// Run the workload as a Kubernetes deployment on Amazon EKS.
    /// </summary>
    Eks,
}

/// <summary>
/// Selects the controller that provisions an EKS workload's public Network Load Balancer.
/// </summary>
public enum AwsEksLoadBalancerMode
{
    /// <summary>
    /// Use the AWS Load Balancer Controller installed in the cluster. This is the default.
    /// </summary>
    AwsLoadBalancerController,

    /// <summary>
    /// Use the load balancing capability built into an EKS Auto Mode cluster.
    /// </summary>
    AutoMode,
}

/// <summary>
/// AWS-specific options for a Fargate compute resource.
/// </summary>
/// <remarks>
/// The EKS backend requires an existing EKS cluster that is already configured for Fargate,
/// along with the cluster's private subnet IDs.
/// </remarks>
public sealed class AwsFargateOptions : IComputeProviderOptions
{
    /// <summary>
    /// Gets the orchestrator used to run the workload. Defaults to Amazon ECS.
    /// </summary>
    public AwsFargateBackend Backend { get; init; } = AwsFargateBackend.Ecs;

    /// <summary>
    /// Gets the name or ARN of the cluster used by the selected backend. For ECS, the existing
    /// cluster is shared when supplied and a per-service cluster is created when omitted. For
    /// EKS, this is the required name of an existing Fargate-ready cluster.
    /// </summary>
    public string? ClusterName { get; init; }

    /// <summary>
    /// Gets the subnet IDs used for Fargate networking. The EKS backend requires private subnet
    /// IDs associated with its existing cluster.
    /// </summary>
    public string[]? SubnetIds { get; init; }

    /// <summary>
    /// Gets the security group IDs assigned to the ECS Fargate workload. EKS security groups
    /// for pods must be configured on the existing cluster and are not accepted here.
    /// </summary>
    public string[]? SecurityGroupIds { get; init; }

    /// <summary>
    /// Gets the Kubernetes namespace used by the EKS backend.
    /// </summary>
    public string? KubernetesNamespace { get; init; }

    /// <summary>
    /// Gets whether the EKS backend creates the Kubernetes namespace. Defaults to <see langword="true"/>.
    /// Set to <see langword="false"/> when <see cref="KubernetesNamespace"/> already exists.
    /// </summary>
    public bool EksCreateNamespace { get; init; } = true;

    /// <summary>
    /// Gets whether the EKS backend creates a CPU-based HorizontalPodAutoscaler when
    /// <c>MaxCount</c> is greater than a positive <c>MinCount</c>. Defaults to
    /// <see langword="true"/>. The existing cluster must have Metrics Server installed when enabled.
    /// </summary>
    public bool EksEnableAutoscaling { get; init; } = true;

    /// <summary>
    /// Gets the controller used to provision the EKS Service load balancer. Defaults to the
    /// AWS Load Balancer Controller; select Auto Mode only for an EKS Auto Mode cluster.
    /// </summary>
    public AwsEksLoadBalancerMode EksLoadBalancerMode { get; init; } =
        AwsEksLoadBalancerMode.AwsLoadBalancerController;

    /// <summary>
    /// Gets an optional kubeconfig used to connect to the existing EKS cluster. When omitted,
    /// the implementation looks up the cluster and creates an exec-authenticated kubeconfig that
    /// refreshes credentials with <c>aws eks get-token</c>.
    /// </summary>
    public Input<string>? Kubeconfig { get; init; }

    /// <summary>
    /// Gets tags applied to AWS resources created for the Fargate workload.
    /// </summary>
    public IDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Gets whether deployment waits for the service or Kubernetes workload to become steady.
    /// </summary>
    public bool WaitForSteadyState { get; init; } = true;
}
