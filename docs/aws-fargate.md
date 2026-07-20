# AWS Fargate compute

`AwsCloudResourceFactory.CreateCompute` creates AWS Fargate services through the common `ICompute` API. Three backends are available through `AwsFargateOptions.Backend`:

| Backend | When to use |
| --- | --- |
| `AwsFargateBackend.EcsExpress` (default) | Least configuration. Express Mode manages ingress, scaling, and networking from a single service resource. |
| `AwsFargateBackend.Ecs` | Standard ECS. Builds the task definition, security groups, Application Load Balancer, service, and autoscaling explicitly, so configuration Express does not expose is reachable. |
| `AwsFargateBackend.Eks` | An existing, Fargate-ready EKS cluster. |

> **Migration note.** `AwsFargateBackend.Ecs` previously selected Express Mode. It now selects the standard ECS backend, and Express Mode is `AwsFargateBackend.EcsExpress`. Code that never set `Backend` is unaffected — Express Mode is still the default. Code that set `Backend = AwsFargateBackend.Ecs` explicitly must change to `AwsFargateBackend.EcsExpress` to keep its previous behavior.

`AwsCloudResourceFactory` derives from `CloudResourceFactory<AwsFargateOptions>` and implements `IComputeFactory<AwsFargateOptions>`, so the provider options type is checked at compile time. The factory signature ends with optional provider-specific options:

```csharp
ICompute CreateCompute(
    string name,
    IContainerRegistry registry,
    string? imageName = null,
    string imageTag = "latest",
    IContainerImage? containerImage = null,
    ComputeServiceType? serviceType = null,
    AwsFargateOptions? providerOptions = null
)
```

Configure the returned `ICompute`, then call `Build()` to create its resources. `Build()` is idempotent for an individual instance.

## Default: ECS Express Mode

With no `serviceType` or `providerOptions`, AWS defaults to Fargate on ECS Express Mode. Each compute service receives its own ECS cluster and a managed public HTTPS endpoint.

```csharp
var factory = new AwsCloudResourceFactory();
var registry = factory.CreateContainerRegistry("api-registry");

var api = factory.CreateCompute(
    name: "api",
    registry: registry,
    imageName: registry.Name,
    imageTag: "latest"
);

api.CpuLimit = "0.5";
api.MemoryLimit = "1Gi";
api.MinCount = 1;
api.MaxCount = 4;
api.PortForwardings =
[
    new PortForwardingConfiguration { ContainerPort = 8080, Protocol = "TCP" },
];

api.Build();
Output<string> url = api.ServiceUrl;
```

An `AwsContainerRegistry` represents one ECR repository, so the direct-image form requires `imageName` to match `registry.Name` and resolves to `<repository-url>:<imageTag>`; the image tag must already exist there. To build and push an image in the same deployment, call `CreateContainerImage`, call `PushImage`, and pass the result as `containerImage`. Fargate then deploys that image's content-addressed `DeploymentResult.ImageUrl` and waits on the image resource.

ECS Express Mode accepts Fargate `CpuLimit` values of `0.25`, `0.5`, `1`, `2`, `4`, `8`, `16`, or `32` vCPU. `MemoryLimit` can range from `512Mi` through `244Gi`; `Mi`, `Gi`, `MB`, and `GB` suffixes are accepted. Set CPU and memory together; the factory validates the pair against AWS Fargate's supported task-size combinations. Omit both to use the Express Mode defaults. `MinCount` and `MaxCount` configure CPU-based service scaling. The managed ingress endpoint is exposed as an HTTPS `ServiceUrl`.

## Reuse a shared ECS cluster

Set `AwsFargateOptions.ClusterName` to place services in an existing ECS cluster. The named cluster is reused and is not created or owned by the compute resource. This applies to both ECS backends.

```csharp
var options = new AwsFargateOptions
{
    ClusterName = "shared-services",
    SubnetIds = ["subnet-0123456789abcdef0", "subnet-0fedcba9876543210"],
    SecurityGroupIds = ["sg-0123456789abcdef0"],
    Tags = new Dictionary<string, string> { ["environment"] = "production" },
};

var api = factory.CreateCompute(
    name: "api",
    registry: registry,
    imageName: registry.Name,
    imageTag: "latest",
    serviceType: ComputeServiceType.Fargate,
    providerOptions: options
);

api.Build();
```

Omit `SubnetIds` and `SecurityGroupIds` to use ECS Express Mode's managed network defaults; this requires a default VPC with at least two suitable public subnets. Accounts without a default VPC must supply at least two subnet IDs. `WaitForSteadyState` defaults to `true` and can be disabled for deployments that should not wait for service convergence.

## Standard ECS services

`AwsFargateBackend.Ecs` provisions the pieces Express Mode keeps internal: an ECS task definition, a security group for the tasks, an Application Load Balancer with a target group and listener, the ECS service itself, and an Application Auto Scaling policy. Choose it when the workload needs multiple port mappings, a custom certificate, ECS Exec, an existing VPC, or any other knob Express does not expose.

```csharp
var options = new AwsFargateOptions
{
    Backend = AwsFargateBackend.Ecs,
    VpcId = "vpc-0123456789abcdef0",
    SubnetIds = ["subnet-0123456789abcdef0", "subnet-0fedcba9876543210"],
    EcsCertificateArn = "arn:aws:acm:us-east-1:123456789012:certificate/abcd-1234",
};

var api = factory.CreateCompute(
    name: "api",
    registry: registry,
    imageName: registry.Name,
    imageTag: "latest",
    serviceType: ComputeServiceType.Fargate,
    providerOptions: options
);

api.CpuLimit = "0.5";
api.MemoryLimit = "1Gi";
api.MinCount = 2;
api.MaxCount = 8;
api.Build();
```

### Networking

Supply `SubnetIds` to place tasks in specific subnets; the VPC is taken from `VpcId`, or resolved from the first subnet when `VpcId` is omitted. With neither, the backend selects the subnets of the account's default VPC.

Tasks are given public IPs only when the backend chose the subnets itself, because default-VPC subnets are public and a task there cannot reach ECR without either a public IP or a NAT gateway. Set `EcsAssignPublicIp` explicitly to override that inference in either direction.

When `SecurityGroupIds` is omitted, the backend creates a task security group that allows all egress and accepts the primary container port from the load balancer. When you do supply security groups they are used as-is: they are yours, so the backend does not add ingress rules to them, and allowing load balancer traffic is your responsibility.

### Ingress

`EcsCreateLoadBalancer` defaults to `true` and requires at least two subnets in different availability zones. Setting `EcsCertificateArn` moves the default action to an HTTPS listener on port 443 and turns port 80 into a redirect; `ServiceUrl` reflects the resulting scheme. `EcsInternalLoadBalancer` makes the balancer internal rather than internet-facing.

Set `EcsCreateLoadBalancer = false` for internal or worker services that need no ingress. The load balancer, target group, listener, and load balancer security group are then skipped — and because there is no endpoint, `ServiceUrl` is empty, `GetRequiredDnsRecords` has no target, and `OnHealthy` callbacks have nothing to poll. `OnReady` still runs.

The target group's health check is taken from `HealthCheckConfiguration` and accepts any `200-399` response. Its timeout must be shorter than its interval, which the backend validates before deploying.

### Scaling, exec, and deployment safety

When `EcsEnableAutoscaling` is `true` (the default) and `MaxCount` is greater than `MinCount`, the backend registers an Application Auto Scaling target and a CPU target-tracking policy at 60% utilization. Because Application Auto Scaling then owns the desired count, the service ignores subsequent `desiredCount` changes so that deployments do not reset a scaled-out service to `MinCount`.

`EcsEnableExecuteCommand` turns on ECS Exec. It always creates a task role and grants it the four `ssmmessages` channel actions the exec agent needs, so `GetPolicy(ComputePermissions.Connect)` returns a usable `ecs:ExecuteCommand` grant.

`EcsEnableCircuitBreaker` defaults to `true`, so a failing deployment rolls back rather than retrying indefinitely.

Task sizing follows the same Fargate rules as Express Mode. Unlike Express, ECS requires a task size, so omitting both `CpuLimit` and `MemoryLimit` yields 0.25 vCPU and 512Mi.

## Use an existing EKS cluster

EKS is an opt-in backend. PulumiCrossCloud does **not** create an EKS cluster. It creates the workload's Fargate profile, pod execution role, namespace, Kubernetes Deployment, and public `LoadBalancer` Service in the supplied cluster.

```csharp
var options = new AwsFargateOptions
{
    Backend = AwsFargateBackend.Eks,
    ClusterName = "platform-production",
    SubnetIds = ["subnet-0123456789abcdef0", "subnet-0fedcba9876543210"],
    KubernetesNamespace = "api",
};

var api = factory.CreateCompute(
    name: "api",
    registry: registry,
    imageName: registry.Name,
    imageTag: "latest",
    serviceType: ComputeServiceType.Fargate,
    providerOptions: options
);

api.MinCount = 2;
api.MaxCount = 6;
api.Build();
```

`ClusterName` and non-empty, private `SubnetIds` are required for EKS. `SecurityGroupIds` applies only to ECS; configure EKS security groups for pods on the existing cluster instead.

`Kubeconfig` is optional and accepts an `Input<string>`. When omitted, the implementation looks up the cluster endpoint and certificate, then generates a refreshable exec-authenticated kubeconfig that runs `aws eks get-token`. The deployment environment therefore needs the AWS CLI on `PATH`, credentials compatible with the selected Pulumi AWS provider, network access to the Kubernetes API endpoint, and Kubernetes authorization in the cluster. Supply `Kubeconfig` explicitly when those credential contexts differ.

By default, the backend creates `KubernetesNamespace` (or a service-named namespace when it is omitted). Set `EksCreateNamespace = false` to reuse an existing namespace; the namespace is then referenced but not created or owned by this compute resource.

When `MaxCount` is greater than a positive `MinCount`, `EksEnableAutoscaling = true` (the default) creates a CPU-based HorizontalPodAutoscaler. Set it to `false` to keep a fixed `MinCount` replica count and avoid the Metrics Server dependency. A zero `MinCount` creates a scaled-to-zero Deployment without an HPA.

The first Kubernetes Service port defaults to public port `80` and forwards to the primary container port. Set `PortForwardingConfiguration.ExternalPort` to expose a different port; non-default ports are included in `ServiceUrl`.

The default `EksLoadBalancerMode` is `AwsEksLoadBalancerMode.AwsLoadBalancerController`. For an EKS Auto Mode cluster, select Auto Mode instead:

```csharp
var options = new AwsFargateOptions
{
    Backend = AwsFargateBackend.Eks,
    ClusterName = "platform-production",
    SubnetIds = ["subnet-0123456789abcdef0", "subnet-0fedcba9876543210"],
    KubernetesNamespace = "shared-workloads",
    EksCreateNamespace = false,
    EksLoadBalancerMode = AwsEksLoadBalancerMode.AutoMode,
};
```

### EKS prerequisites

- The named EKS cluster must already exist and be ready for Fargate workloads. This API never auto-creates an EKS cluster.
- Supply private subnet IDs suitable for EKS Fargate profiles.
- The deployment's AWS identity needs EKS lookup/authentication permissions and Kubernetes access to create namespaces, secrets, Deployments, HPAs, and Services.
- For the default load balancer mode, install and configure AWS Load Balancer Controller. Alternatively, use an EKS Auto Mode cluster and set `EksLoadBalancerMode = AwsEksLoadBalancerMode.AutoMode`. Both generated Service variants request an internet-facing Network Load Balancer with IP targets for Fargate pods.
- Install Kubernetes Metrics Server when `EksEnableAutoscaling` is enabled and `MaxCount` is greater than a positive `MinCount`; the generated HPA scales on CPU utilization.
- Custom AWS workload policies on EKS require IRSA. `AddPolicyStatement` is not automatically translated to IRSA for this backend, so configure the service account, role, and trust relationship separately.

For EKS, `GetPolicy` returns only AWS-side permissions that can be represented accurately: cluster and Fargate-profile description, plus Fargate-profile deletion. Starting, stopping, scaling, connecting to, reconfiguring, and reading logs from the workload are Kubernetes API operations and require appropriate Kubernetes access and RBAC. The helper intentionally does not return `eks:AccessKubernetesApi`; that permission controls the AWS console's Kubernetes-resource view and does not authorize ordinary Kubernetes API clients.

When `WaitForSteadyState` is `false`, the namespace (when created), Deployment, and Service receive Pulumi's `skipAwait` annotation. The EKS `ServiceUrl` is the load balancer endpoint. TLS certificates and custom HTTPS configuration for that endpoint remain the responsibility of the existing EKS platform and its selected load balancer controller.
