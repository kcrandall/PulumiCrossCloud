using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

/// <summary>
/// Selects the AWS container orchestrator used to run a Fargate workload.
/// </summary>
public enum AwsFargateBackend
{
    /// <summary>
    /// Run the workload as an Amazon ECS Express Mode service. Express provisions its own
    /// ingress and scaling from a single resource, so it needs the least configuration.
    /// This is the default backend.
    /// </summary>
    EcsExpress,

    /// <summary>
    /// Run the workload as a standard Amazon ECS service backed by an explicit task
    /// definition, security group, Application Load Balancer, and Application Auto Scaling
    /// policy. Choose this when the workload needs configuration Express Mode does not
    /// expose, such as multiple port mappings, a custom certificate, ECS Exec, or an
    /// existing VPC.
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
/// along with the cluster's private subnet IDs. Options prefixed <c>Ecs</c> apply only to the
/// standard <see cref="AwsFargateBackend.Ecs"/> backend and are ignored by Express Mode, which
/// manages the equivalent infrastructure itself.
/// </remarks>
public sealed class AwsFargateOptions : IComputeProviderOptions
{
    /// <summary>
    /// Gets the orchestrator used to run the workload. Defaults to ECS Express Mode.
    /// </summary>
    public AwsFargateBackend Backend { get; init; } = AwsFargateBackend.EcsExpress;

    /// <summary>
    /// Gets the name or ARN of the cluster used by the selected backend. For either ECS
    /// backend, the existing cluster is shared when supplied and a per-service cluster is
    /// created when omitted. For EKS, this is the required name of an existing Fargate-ready
    /// cluster.
    /// </summary>
    public string? ClusterName { get; init; }

    /// <summary>
    /// Gets the subnet IDs used for Fargate networking. The EKS backend requires private subnet
    /// IDs associated with its existing cluster. The standard ECS backend falls back to the
    /// subnets of the account's default VPC when this is omitted.
    /// </summary>
    public string[]? SubnetIds { get; init; }

    /// <summary>
    /// Gets the security group IDs assigned to the ECS Fargate workload. The standard ECS
    /// backend creates a security group when this is omitted. EKS security groups for pods must
    /// be configured on the existing cluster and are not accepted here.
    /// </summary>
    public string[]? SecurityGroupIds { get; init; }

    /// <summary>
    /// Gets the VPC hosting the standard ECS backend's load balancer and target group. When
    /// omitted, the VPC is resolved from <see cref="SubnetIds"/>, or from the account's default
    /// VPC when no subnets are supplied.
    /// </summary>
    public string? VpcId { get; init; }

    /// <summary>
    /// Gets whether the standard ECS backend provisions an Application Load Balancer for the
    /// service. Defaults to <see langword="true"/>. Set to <see langword="false"/> for internal
    /// or worker services that need no ingress; <c>ServiceUrl</c> is then empty and
    /// <c>OnHealthy</c> callbacks and <c>GetRequiredDnsRecords</c> have no endpoint to target.
    /// </summary>
    public bool EcsCreateLoadBalancer { get; init; } = true;

    /// <summary>
    /// Gets whether the standard ECS backend's load balancer is internal rather than
    /// internet-facing. Defaults to <see langword="false"/>.
    /// </summary>
    public bool EcsInternalLoadBalancer { get; init; }

    /// <summary>
    /// Gets an ACM certificate ARN for the standard ECS backend's load balancer. When supplied,
    /// the listener serves HTTPS on port 443 and port 80 redirects to it.
    /// </summary>
    public Input<string>? EcsCertificateArn { get; init; }

    /// <summary>
    /// Gets whether the standard ECS backend assigns public IPs to tasks. When omitted, public
    /// IPs are assigned only if the subnets were resolved from the default VPC, because tasks in
    /// public subnets cannot reach ECR without either a public IP or a NAT gateway.
    /// </summary>
    public bool? EcsAssignPublicIp { get; init; }

    /// <summary>
    /// Gets whether the standard ECS backend enables ECS Exec. Defaults to
    /// <see langword="false"/>. When enabled, a task role is always created and granted the
    /// SSM messages permissions the exec agent requires.
    /// </summary>
    public bool EcsEnableExecuteCommand { get; init; }

    /// <summary>
    /// Gets whether the standard ECS backend enables the deployment circuit breaker with
    /// automatic rollback. Defaults to <see langword="true"/>.
    /// </summary>
    public bool EcsEnableCircuitBreaker { get; init; } = true;

    /// <summary>
    /// Gets whether the standard ECS backend creates a CPU target-tracking Application Auto
    /// Scaling policy when <c>MaxCount</c> is greater than <c>MinCount</c>. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool EcsEnableAutoscaling { get; init; } = true;

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
