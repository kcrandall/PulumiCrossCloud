using System.Collections.Concurrent;
using System.Text.Json;
using Pulumi;
using Pulumi.Testing;
using PulumiCrossCloud.Core;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PulumiCrossCloud.Aws.Tests;

public class AwsFargateComputeTests
{
    private const string EcsClusterType = "aws:ecs/cluster:Cluster";
    private const string EcsExpressGatewayServiceType =
        "aws:ecs/expressGatewayService:ExpressGatewayService";
    private const string EcsServiceType = "aws:ecs/service:Service";
    private const string EcsTaskDefinitionType = "aws:ecs/taskDefinition:TaskDefinition";
    private const string LoadBalancerType = "aws:lb/loadBalancer:LoadBalancer";
    private const string TargetGroupType = "aws:lb/targetGroup:TargetGroup";
    private const string ListenerType = "aws:lb/listener:Listener";
    private const string SecurityGroupType = "aws:ec2/securityGroup:SecurityGroup";
    private const string AutoScalingTargetType = "aws:appautoscaling/target:Target";
    private const string AutoScalingPolicyType = "aws:appautoscaling/policy:Policy";
    private const string EksFargateProfileType = "aws:eks/fargateProfile:FargateProfile";
    private const string KubernetesProviderType = "pulumi:providers:kubernetes";
    private const string KubernetesNamespaceType = "kubernetes:core/v1:Namespace";
    private const string KubernetesDeploymentType = "kubernetes:apps/v1:Deployment";
    private const string KubernetesHorizontalPodAutoscalerType =
        "kubernetes:autoscaling/v2:HorizontalPodAutoscaler";
    private const string KubernetesServiceType = "kubernetes:core/v1:Service";
    private const string SecretsManagerSecretType = "aws:secretsmanager/secret:Secret";
    private const string SecretsManagerSecretVersionType =
        "aws:secretsmanager/secretVersion:SecretVersion";

    private const string TestKubeconfig = """
        apiVersion: v1
        kind: Config
        current-context: shared-eks
        clusters:
          - name: shared-eks
            cluster:
              server: https://shared-eks.example.test
              insecure-skip-tls-verify: true
        contexts:
          - name: shared-eks
            context:
              cluster: shared-eks
              user: shared-eks
        users:
          - name: shared-eks
            user:
              token: test-token
        """;

    [Fact]
    public async Task Factory_DefaultsToAwsFargateCompute()
    {
        ICompute? compute = null;

        await RunDeployment((factory, registry) =>
        {
            compute = factory.CreateCompute("web", registry, imageName: registry.Name);
        });

        var fargate = Assert.IsType<AwsFargateCompute>(compute);
        Assert.Equal(ComputeServiceType.Fargate, fargate.ServiceType);
    }

    [Fact]
    public async Task Factory_DirectImageNameMustMatchTheEcrRepository()
    {
        await RunDeployment((factory, registry) =>
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                factory.CreateCompute("web", registry, imageName: "different-repository")
            );

            Assert.Equal("imageName", exception.ParamName);
            Assert.Contains("must match", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Build_DefaultEcsBackend_CreatesClusterAndExpressGatewayService()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute("orders", registry, imageName: registry.Name);
            compute.Build();
        });

        var cluster = Assert.Single(mocks.CreatedResources(EcsClusterType));
        Assert.Equal("orders-cluster", cluster.Name);

        var service = Assert.Single(mocks.CreatedResources(EcsExpressGatewayServiceType));
        Assert.Equal("orders-service", service.Name);
        Assert.Equal(
            "arn:aws:ecs:us-east-1:123456789012:cluster/orders-cluster",
            Assert.IsType<string>(service.Inputs["cluster"])
        );
    }

    [Fact]
    public async Task Build_WithSharedEcsCluster_DoesNotCreateClusterAndUsesSuppliedName()
    {
        const string clusterName = "shared-cluster";
        var mocks = await RunDeployment((factory, registry) =>
        {
            IComputeFactory<AwsFargateOptions> typedFactory = factory;
            var compute = typedFactory.CreateCompute(
                "orders",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions { ClusterName = clusterName }
            );
            compute.Build();
        });

        Assert.Empty(mocks.CreatedResources(EcsClusterType));
        var service = Assert.Single(mocks.CreatedResources(EcsExpressGatewayServiceType));
        Assert.Equal(clusterName, Assert.IsType<string>(service.Inputs["cluster"]));
    }

    [Fact]
    public async Task Build_WithoutOptions_CreatesSeparateClusterForEachService()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            factory.CreateCompute("api", registry, imageName: registry.Name).Build();
            factory.CreateCompute("worker", registry, imageName: registry.Name).Build();
        });

        var clusterNames = mocks
            .CreatedResources(EcsClusterType)
            .Select(resource => resource.Name ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["api-cluster", "worker-cluster"], clusterNames);
    }

    [Fact]
    public async Task Build_WithRequiredSecret_DependsOnSecretValueVersion()
    {
        AwsSecret? awsSecret = null;
        TrackingSecret? trackingSecret = null;

        var mocks = await RunDeployment((factory, registry) =>
        {
            awsSecret = Assert.IsType<AwsSecret>(
                factory.CreateSecret("database-password", "test-value")
            );
            trackingSecret = new TrackingSecret(awsSecret);

            var compute = factory.CreateCompute("orders", registry, imageName: registry.Name);
            compute.AddRequiredSecret(trackingSecret, "DATABASE_PASSWORD");
            compute.Build();
        });

        Assert.NotNull(awsSecret);
        Assert.NotNull(trackingSecret);
        Assert.True(trackingSecret.ValueResourceWasRead);
        Assert.IsType<Pulumi.Aws.SecretsManager.SecretVersion>(awsSecret.ValueResource);
        Assert.Single(mocks.CreatedResources(SecretsManagerSecretType));
        Assert.Single(mocks.CreatedResources(SecretsManagerSecretVersionType));
        Assert.Single(mocks.CreatedResources(EcsExpressGatewayServiceType));
    }

    [Fact]
    public async Task Build_WithContainerImage_UsesItsDeployedImageUrl()
    {
        const string imageUrl =
            "123456789012.dkr.ecr.us-east-1.amazonaws.com/orders:sha-12345678";
        var mocks = await RunDeployment((factory, registry) =>
        {
            var containerImage = new TestContainerImage(registry, imageUrl);
            var compute = factory.CreateCompute(
                "orders",
                registry,
                containerImage: containerImage
            );
            compute.Build();
        });

        var service = Assert.Single(mocks.CreatedResources(EcsExpressGatewayServiceType));
        Assert.Contains(
            imageUrl,
            JsonSerializer.Serialize(service.Inputs),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task AwsContainerImage_PushImage_UsesTheEcrRepositoryUri()
    {
        var contextPath = Path.Combine(
            Path.GetTempPath(),
            $"pulumi-cross-cloud-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(contextPath);
        var dockerfilePath = Path.Combine(contextPath, "Dockerfile");
        await File.WriteAllTextAsync(
            dockerfilePath,
            "FROM public.ecr.aws/nginx/nginx:alpine\n"
        );

        try
        {
            var mocks = await RunDeployment((factory, registry) =>
            {
                var image = factory.CreateContainerImage("orders-image", registry);
                image.PushImage(
                    dockerfilePath,
                    contextPath,
                    new Pulumi.DockerBuild.ImageArgs
                    {
                        NoCache = true,
                        Push = false,
                        Tags = ["example.invalid/not-pushed:latest"],
                    }
                );

                var compute = factory.CreateCompute(
                    "orders",
                    registry,
                    containerImage: image
                );
                compute.Build();
            });

            var service = Assert.Single(mocks.CreatedResources(EcsExpressGatewayServiceType));
            var serviceJson = JsonSerializer.Serialize(service.Inputs);
            Assert.Matches("test-registry:sha-[0-9a-f]{8}", serviceJson);
            Assert.DoesNotContain(
                "test-registry/orders-image",
                serviceJson,
                StringComparison.Ordinal
            );

            var imageResource = Assert.Single(
                mocks.CreatedResources("docker-build:index:Image")
            );
            var imageJson = JsonSerializer.Serialize(imageResource.Inputs);
            Assert.Contains("\"push\":true", imageJson, StringComparison.Ordinal);
            Assert.Contains("\"noCache\":true", imageJson, StringComparison.Ordinal);
            Assert.Matches("test-registry:sha-[0-9a-f]{8}", imageJson);
            Assert.DoesNotContain("example.invalid/not-pushed", imageJson, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(contextPath, recursive: true);
        }
    }

    [Fact]
    public void Factory_ExposesTypedOptionalProviderOptionsContract()
    {
        Assert.True(
            typeof(IComputeFactory<AwsFargateOptions>).IsAssignableFrom(
                typeof(AwsCloudResourceFactory)
            )
        );

        var createCompute = Assert.Single(
            typeof(IComputeFactory<AwsFargateOptions>).GetMethods(),
            method => method.Name == nameof(IComputeFactory<AwsFargateOptions>.CreateCompute)
        );
        var providerOptions = Assert.Single(
            createCompute.GetParameters(),
            parameter => parameter.Name == "providerOptions"
        );

        Assert.Equal(typeof(AwsFargateOptions), providerOptions.ParameterType);
        Assert.True(providerOptions.IsOptional);
    }

    [Fact]
    public async Task Factory_CloudAgnosticBaseCall_PreservesDefaultComputeBehavior()
    {
        ICompute? compute = null;

        await RunDeployment((factory, registry) =>
        {
            CloudResourceFactory cloudAgnosticFactory = factory;
            compute = cloudAgnosticFactory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name
            );
        });

        Assert.IsType<AwsFargateCompute>(compute);
    }

    [Fact]
    public async Task EcsPolicies_ContainExpressActionsAndNoAppRunnerActions()
    {
        string[] actions = [];
        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute("web", registry, imageName: registry.Name);
            actions = compute
                .GetPolicy(Enum.GetValues<ComputePermissions>())
                .SelectMany(statement => statement.Actions)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        });

        Assert.Contains("ecs:DescribeExpressGatewayService", actions);
        Assert.Contains("ecs:UpdateExpressGatewayService", actions);
        Assert.Contains("ecs:DeleteExpressGatewayService", actions);
        Assert.Contains(actions, action => action.StartsWith("ecs:", StringComparison.Ordinal));
        Assert.DoesNotContain(
            actions,
            action => action.Contains("apprunner", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task EksBackend_RequiresClusterName()
    {
        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Eks,
                    SubnetIds = ["subnet-a"],
                }
            );

            var exception = Assert.Throws<ArgumentException>(compute.Build);
            Assert.Equal("ClusterName", exception.ParamName);
        });
    }

    [Fact]
    public async Task EksBackend_RequiresSubnets()
    {
        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Eks,
                    ClusterName = "shared-eks",
                }
            );

            var exception = Assert.Throws<ArgumentException>(compute.Build);
            Assert.Equal("SubnetIds", exception.ParamName);
        });
    }

    [Fact]
    public async Task EksBackend_RegistersFargateProfileAndKubernetesWorkloadOnly()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Eks,
                    ClusterName = "shared-eks",
                    SubnetIds = ["subnet-a", "subnet-b"],
                    Kubeconfig = TestKubeconfig,
                    WaitForSteadyState = false,
                }
            );
            compute.Build();
        });

        var profile = Assert.Single(mocks.CreatedResources(EksFargateProfileType));
        Assert.Equal("shared-eks", Assert.IsType<string>(profile.Inputs["clusterName"]));
        Assert.Single(mocks.CreatedResources(KubernetesDeploymentType));
        Assert.Single(mocks.CreatedResources(KubernetesServiceType));
        Assert.Empty(mocks.CreatedResources(EcsClusterType));
        Assert.Empty(mocks.CreatedResources(EcsExpressGatewayServiceType));
    }

    [Fact]
    public async Task EksBackend_HealthProbesUseKubernetesSuccessThresholdRules()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Eks,
                    ClusterName = "shared-eks",
                    SubnetIds = ["subnet-a", "subnet-b"],
                    Kubeconfig = TestKubeconfig,
                }
            );
            compute.HealthCheckConfiguration = new HealthCheckConfiguration
            {
                HealthyThreshold = 3,
                StartupIntervalSeconds = 5,
            };
            compute.Build();
        });

        var deployment = Assert.Single(mocks.CreatedResources(KubernetesDeploymentType));
        using var deploymentDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(deployment.Inputs)
        );
        var container = deploymentDocument
            .RootElement.GetProperty("spec")
            .GetProperty("template")
            .GetProperty("spec")
            .GetProperty("containers")[0];

        Assert.Equal(
            3,
            container.GetProperty("readinessProbe").GetProperty("successThreshold").GetInt32()
        );
        Assert.Equal(
            1,
            container.GetProperty("livenessProbe").GetProperty("successThreshold").GetInt32()
        );
        Assert.Equal(
            1,
            container.GetProperty("startupProbe").GetProperty("successThreshold").GetInt32()
        );
    }

    [Fact]
    public async Task EksPoliciesUseClusterAndFargateProfileResourcesWithoutConsoleProxyPermission()
    {
        string[] actions = [];
        var resourcesByAction = new ConcurrentDictionary<string, string>();

        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Eks,
                    ClusterName = "shared-eks",
                    SubnetIds = ["subnet-a", "subnet-b"],
                    Kubeconfig = TestKubeconfig,
                }
            );
            compute.Build();

            var statements = compute.GetPolicy(Enum.GetValues<ComputePermissions>()).ToArray();
            actions = statements.SelectMany(statement => statement.Actions).ToArray();
            foreach (var statement in statements)
                foreach (var action in statement.Actions)
                    foreach (var resource in statement.Resources)
                        resource.Apply(value =>
                        {
                            resourcesByAction[action] = value;
                            return value;
                        });
        });

        Assert.Equal(
            [
                "eks:DeleteFargateProfile",
                "eks:DescribeCluster",
                "eks:DescribeFargateProfile",
            ],
            actions.Order(StringComparer.Ordinal).ToArray()
        );
        Assert.DoesNotContain("eks:AccessKubernetesApi", actions);
        Assert.Equal(
            "arn:aws:eks:us-east-1:123456789012:cluster/shared-eks",
            resourcesByAction["eks:DescribeCluster"]
        );
        Assert.Equal(
            resourcesByAction["eks:DescribeFargateProfile"],
            resourcesByAction["eks:DeleteFargateProfile"]
        );
        Assert.StartsWith(
            "arn:aws:eks:us-east-1:123456789012:fargateprofile/shared-eks/",
            resourcesByAction["eks:DescribeFargateProfile"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task EcsBackend_CustomNetworkingRequiresTwoSubnets()
    {
        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions { SubnetIds = ["subnet-a"] }
            );

            var exception = Assert.Throws<ArgumentException>(compute.Build);
            Assert.Equal("SubnetIds", exception.ParamName);
        });
    }

    [Theory]
    [InlineData("4", "16Gi", "4096", "16384")]
    [InlineData("8", "60Gi", "8192", "61440")]
    [InlineData("16", "120Gi", "16384", "122880")]
    [InlineData("32", "244Gi", "32768", "249856")]
    public async Task EcsBackend_AcceptsSupportedFargateTaskSize(
        string cpuLimit,
        string memoryLimit,
        string expectedCpu,
        string expectedMemory
    )
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute("web", registry, imageName: registry.Name);
            compute.CpuLimit = cpuLimit;
            compute.MemoryLimit = memoryLimit;
            compute.Build();
        });

        var service = Assert.Single(mocks.CreatedResources(EcsExpressGatewayServiceType));
        Assert.Equal(expectedCpu, Assert.IsType<string>(service.Inputs["cpu"]));
        Assert.Equal(expectedMemory, Assert.IsType<string>(service.Inputs["memory"]));
    }

    [Fact]
    public async Task EcsBackend_RejectsUnsupportedFargateTaskSizeCombination()
    {
        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute("web", registry, imageName: registry.Name);
            compute.CpuLimit = "0.25";
            compute.MemoryLimit = "4Gi";

            var exception = Assert.Throws<InvalidOperationException>(compute.Build);
            Assert.Contains("not a supported", exception.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("1", null)]
    [InlineData(null, "2Gi")]
    public async Task EcsBackend_RequiresCpuAndMemoryLimitsTogether(
        string? cpuLimit,
        string? memoryLimit
    )
    {
        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute("web", registry, imageName: registry.Name);
            compute.CpuLimit = cpuLimit;
            compute.MemoryLimit = memoryLimit;

            var exception = Assert.Throws<InvalidOperationException>(compute.Build);
            Assert.Contains("set together", exception.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("0.2509", "512Mi")]
    [InlineData("0.25", "512.9Mi")]
    public async Task EcsBackend_RejectsFractionalCpuUnitsOrMebibytes(
        string cpuLimit,
        string memoryLimit
    )
    {
        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute("web", registry, imageName: registry.Name);
            compute.CpuLimit = cpuLimit;
            compute.MemoryLimit = memoryLimit;

            Assert.Throws<InvalidOperationException>(compute.Build);
        });
    }

    [Fact]
    public async Task EksBackend_RejectsEcsSecurityGroups()
    {
        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Eks,
                    ClusterName = "shared-eks",
                    SubnetIds = ["subnet-a"],
                    SecurityGroupIds = ["sg-a"],
                }
            );

            var exception = Assert.Throws<ArgumentException>(compute.Build);
            Assert.Equal("SecurityGroupIds", exception.ParamName);
        });
    }

    [Fact]
    public async Task EksBackend_DefaultsCreateNamespaceAutoscalerAndAwsControllerService()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Eks,
                    ClusterName = "shared-eks",
                    SubnetIds = ["subnet-a", "subnet-b"],
                    Kubeconfig = TestKubeconfig,
                }
            );
            compute.MaxCount = 3;
            compute.Build();
        });

        Assert.Single(mocks.CreatedResources(KubernetesNamespaceType));
        Assert.Single(mocks.CreatedResources(KubernetesHorizontalPodAutoscalerType));
        var service = Assert.Single(mocks.CreatedResources(KubernetesServiceType));
        var serviceJson = JsonSerializer.Serialize(service.Inputs);
        Assert.Contains("aws-load-balancer-type", serviceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("eks.amazonaws.com/nlb", serviceJson, StringComparison.Ordinal);
        Assert.Contains("\"port\":80", serviceJson, StringComparison.Ordinal);
        Assert.Contains("\"targetPort\":8080", serviceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EksBackend_CanReuseNamespaceDisableAutoscalingAndUseAutoMode()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Eks,
                    ClusterName = "shared-eks",
                    SubnetIds = ["subnet-a", "subnet-b"],
                    Kubeconfig = TestKubeconfig,
                    KubernetesNamespace = "shared-workloads",
                    EksCreateNamespace = false,
                    EksEnableAutoscaling = false,
                    EksLoadBalancerMode = AwsEksLoadBalancerMode.AutoMode,
                    WaitForSteadyState = false,
                }
            );
            compute.MaxCount = 3;
            compute.Build();
        });

        Assert.Empty(mocks.CreatedResources(KubernetesNamespaceType));
        Assert.Empty(mocks.CreatedResources(KubernetesHorizontalPodAutoscalerType));

        var deployment = Assert.Single(mocks.CreatedResources(KubernetesDeploymentType));
        var deploymentJson = JsonSerializer.Serialize(deployment.Inputs);
        Assert.Contains("pulumi.com/skipAwait", deploymentJson, StringComparison.Ordinal);

        var service = Assert.Single(mocks.CreatedResources(KubernetesServiceType));
        var serviceJson = JsonSerializer.Serialize(service.Inputs);
        Assert.Contains("eks.amazonaws.com/nlb", serviceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("aws-load-balancer-type", serviceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EksBackend_GeneratedKubeconfigUsesRefreshableAwsExecAuthentication()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Eks,
                    ClusterName = "shared-eks",
                    SubnetIds = ["subnet-a", "subnet-b"],
                    EksCreateNamespace = false,
                }
            );
            compute.Build();
        });

        var provider = Assert.Single(mocks.CreatedResources(KubernetesProviderType));
        var kubeconfig = Assert.IsType<string>(provider.Inputs["kubeconfig"]);
        Assert.Contains("\"command\":\"aws\"", kubeconfig, StringComparison.Ordinal);
        Assert.Contains("get-token", kubeconfig, StringComparison.Ordinal);
        Assert.Contains("us-east-1", kubeconfig, StringComparison.Ordinal);
        Assert.DoesNotContain("\"token\":", kubeconfig, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardEcsBackend_CreatesTaskDefinitionServiceAndPublicLoadBalancer()
    {
        string? serviceUrl = null;

        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "orders",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions { Backend = AwsFargateBackend.Ecs }
            );
            compute.Build();
            compute.ServiceUrl.Apply(url => serviceUrl = url);
        });

        Assert.Single(mocks.CreatedResources(EcsClusterType));
        Assert.Empty(mocks.CreatedResources(EcsExpressGatewayServiceType));

        var service = Assert.Single(mocks.CreatedResources(EcsServiceType));
        Assert.Equal("orders", Assert.IsType<string>(service.Inputs["name"]));
        Assert.Equal("FARGATE", Assert.IsType<string>(service.Inputs["launchType"]));

        var taskDefinition = Assert.Single(mocks.CreatedResources(EcsTaskDefinitionType));
        Assert.Equal("awsvpc", Assert.IsType<string>(taskDefinition.Inputs["networkMode"]));
        Assert.Equal("256", Assert.IsType<string>(taskDefinition.Inputs["cpu"]));
        Assert.Equal("512", Assert.IsType<string>(taskDefinition.Inputs["memory"]));

        Assert.Single(mocks.CreatedResources(LoadBalancerType));
        Assert.Single(mocks.CreatedResources(TargetGroupType));
        var listener = Assert.Single(mocks.CreatedResources(ListenerType));
        Assert.Equal("HTTP", Assert.IsType<string>(listener.Inputs["protocol"]));

        // One security group for the load balancer, one for the tasks.
        Assert.Equal(2, mocks.CreatedResources(SecurityGroupType).Count());

        Assert.NotNull(serviceUrl);
        Assert.StartsWith("http://", serviceUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardEcsBackend_WithoutLoadBalancer_SkipsIngressResources()
    {
        string? serviceUrl = null;

        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "worker",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Ecs,
                    EcsCreateLoadBalancer = false,
                }
            );
            compute.Build();
            compute.ServiceUrl.Apply(url => serviceUrl = url);
        });

        Assert.Single(mocks.CreatedResources(EcsServiceType));
        Assert.Empty(mocks.CreatedResources(LoadBalancerType));
        Assert.Empty(mocks.CreatedResources(TargetGroupType));
        Assert.Empty(mocks.CreatedResources(ListenerType));
        Assert.Single(mocks.CreatedResources(SecurityGroupType));
        Assert.True(string.IsNullOrEmpty(serviceUrl));
    }

    [Fact]
    public async Task StandardEcsBackend_WithCertificate_ServesHttpsAndRedirectsHttp()
    {
        string? serviceUrl = null;

        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "orders",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Ecs,
                    EcsCertificateArn =
                        "arn:aws:acm:us-east-1:123456789012:certificate/test-cert",
                }
            );
            compute.Build();
            compute.ServiceUrl.Apply(url => serviceUrl = url);
        });

        var listeners = mocks.CreatedResources(ListenerType).ToArray();
        Assert.Equal(2, listeners.Length);

        var https = Assert.Single(
            listeners,
            listener => Assert.IsType<string>(listener.Inputs["protocol"]) == "HTTPS"
        );
        Assert.Equal(443.0, Convert.ToDouble(https.Inputs["port"]));

        var http = Assert.Single(
            listeners,
            listener => Assert.IsType<string>(listener.Inputs["protocol"]) == "HTTP"
        );
        var defaultActions = Assert.IsAssignableFrom<IEnumerable<object>>(
            http.Inputs["defaultActions"]
        );
        var action = Assert.IsAssignableFrom<IDictionary<string, object>>(
            defaultActions.Single()
        );
        Assert.Equal("redirect", action["type"]);

        Assert.NotNull(serviceUrl);
        Assert.StartsWith("https://", serviceUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardEcsBackend_LoadBalancerRequiresTwoSubnets()
    {
        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "web",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Ecs,
                    SubnetIds = ["subnet-a"],
                }
            );

            var exception = Assert.Throws<ArgumentException>(compute.Build);
            Assert.Equal("SubnetIds", exception.ParamName);
        });
    }

    [Fact]
    public async Task StandardEcsBackend_ContainerDefinitionsCarryEnvironmentAndSecrets()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var secret = factory.CreateSecret("database-password", "test-value");
            var compute = factory.CreateCompute(
                "orders",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions { Backend = AwsFargateBackend.Ecs }
            );
            compute.AddEnvironmentVariable("STAGE", "production");
            compute.AddRequiredSecret(secret, "DATABASE_PASSWORD");
            compute.Build();
        });

        var taskDefinition = Assert.Single(mocks.CreatedResources(EcsTaskDefinitionType));
        using var document = JsonDocument.Parse(
            Assert.IsType<string>(taskDefinition.Inputs["containerDefinitions"])
        );

        var container = Assert.Single(document.RootElement.EnumerateArray().ToArray());
        Assert.Equal("orders", container.GetProperty("name").GetString());
        Assert.True(container.GetProperty("essential").GetBoolean());

        var environment = container.GetProperty("environment").EnumerateArray().ToArray();
        var stage = Assert.Single(
            environment,
            entry => entry.GetProperty("name").GetString() == "STAGE"
        );
        Assert.Equal("production", stage.GetProperty("value").GetString());

        var secrets = container.GetProperty("secrets").EnumerateArray().ToArray();
        var databasePassword = Assert.Single(secrets);
        Assert.Equal("DATABASE_PASSWORD", databasePassword.GetProperty("name").GetString());
        Assert.StartsWith(
            "arn:aws:secretsmanager:",
            databasePassword.GetProperty("valueFrom").GetString(),
            StringComparison.Ordinal
        );

        var logOptions = container.GetProperty("logConfiguration").GetProperty("options");
        Assert.Equal("us-east-1", logOptions.GetProperty("awslogs-region").GetString());
        Assert.Equal("orders", logOptions.GetProperty("awslogs-stream-prefix").GetString());

        var portMapping = Assert.Single(
            container.GetProperty("portMappings").EnumerateArray().ToArray()
        );
        Assert.Equal(8080, portMapping.GetProperty("containerPort").GetInt32());
        Assert.Equal("tcp", portMapping.GetProperty("protocol").GetString());
    }

    [Fact]
    public async Task StandardEcsBackend_AutoscalesWhenMaxCountExceedsMinCount()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "orders",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions { Backend = AwsFargateBackend.Ecs }
            );
            compute.MinCount = 2;
            compute.MaxCount = 8;
            compute.Build();
        });

        var target = Assert.Single(mocks.CreatedResources(AutoScalingTargetType));
        Assert.Equal(2.0, Convert.ToDouble(target.Inputs["minCapacity"]));
        Assert.Equal(8.0, Convert.ToDouble(target.Inputs["maxCapacity"]));
        Assert.Equal(
            "ecs:service:DesiredCount",
            Assert.IsType<string>(target.Inputs["scalableDimension"])
        );
        Assert.Single(mocks.CreatedResources(AutoScalingPolicyType));
    }

    [Fact]
    public async Task StandardEcsBackend_DoesNotAutoscaleWhenCountsMatch()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "orders",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions { Backend = AwsFargateBackend.Ecs }
            );
            compute.MinCount = 2;
            compute.MaxCount = 2;
            compute.Build();
        });

        Assert.Empty(mocks.CreatedResources(AutoScalingTargetType));
        Assert.Empty(mocks.CreatedResources(AutoScalingPolicyType));
    }

    [Fact]
    public async Task StandardEcsBackend_PolicyUsesStandardServiceActions()
    {
        string[] actions = [];

        await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "orders",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions { Backend = AwsFargateBackend.Ecs }
            );
            compute.Build();

            actions = compute
                .GetPolicy(Enum.GetValues<ComputePermissions>())
                .SelectMany(statement => statement.Actions)
                .ToArray();
        });

        Assert.Contains("ecs:DescribeServices", actions);
        Assert.Contains("ecs:UpdateService", actions);
        Assert.Contains("ecs:DeleteService", actions);
        Assert.Contains("ecs:ExecuteCommand", actions);
        Assert.Contains("logs:GetLogEvents", actions);
        Assert.DoesNotContain(
            actions,
            action => action.Contains("ExpressGateway", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task StandardEcsBackend_ExecuteCommandGrantsTheAgentItsChannels()
    {
        var mocks = await RunDeployment((factory, registry) =>
        {
            var compute = factory.CreateCompute(
                "orders",
                registry,
                imageName: registry.Name,
                providerOptions: new AwsFargateOptions
                {
                    Backend = AwsFargateBackend.Ecs,
                    EcsEnableExecuteCommand = true,
                }
            );
            compute.Build();
        });

        var service = Assert.Single(mocks.CreatedResources(EcsServiceType));
        Assert.True(Assert.IsType<bool>(service.Inputs["enableExecuteCommand"]));

        var taskPolicy = Assert.Single(
            mocks.CreatedResources("aws:iam/rolePolicy:RolePolicy"),
            policy => policy.Name == "orders-task-policy"
        );
        Assert.Contains(
            "ssmmessages:OpenDataChannel",
            Assert.IsType<string>(taskPolicy.Inputs["policy"]),
            StringComparison.Ordinal
        );
    }

    private static async Task<RecordingMocks> RunDeployment(
        Action<AwsCloudResourceFactory, IContainerRegistry> test
    )
    {
        var mocks = new RecordingMocks();
        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                var factory = new AwsCloudResourceFactory();
                var registry = factory.CreateContainerRegistry(
                    "test-registry",
                    createDefaultImage: false
                );
                test(factory, registry);
            }
        );
        return mocks;
    }

    private sealed class TrackingSecret(ISecret inner) : ISecret
    {
        public bool ValueResourceWasRead { get; private set; }
        public string Name => inner.Name;
        public Output<string> Arn => inner.Arn;
        public CloudProvider Provider => inner.Provider;
        public Output<string?> Value => inner.Value;
        public bool IgnoreChangesAfterCreate => inner.IgnoreChangesAfterCreate;
        public Resource Resource => inner.Resource;

        public Resource ValueResource
        {
            get
            {
                ValueResourceWasRead = true;
                return inner.ValueResource;
            }
        }

        public Task<string> GetValueAsync() => inner.GetValueAsync();

        public void SetValue(string value) => inner.SetValue(value);

        public IEnumerable<IPolicyStatement> GetPolicy(params SecretPermissions[] permissions) =>
            inner.GetPolicy(permissions);
    }

    private sealed class TestContainerImage : IContainerImage
    {
        public TestContainerImage(IContainerRegistry registry, string imageUrl)
        {
            Registry = registry;
            Resource = registry.Resource;
            ContainerImageBuildResource = registry.Resource;
            DeploymentResult = Output.Create(
                new ImageDeploymentResult { ImageUrl = imageUrl, ImageTag = "sha-12345678" }
            );
            Arn = Output.Create(imageUrl);
        }

        public string Name => "test-image";
        public Output<string> Arn { get; }
        public CloudProvider Provider => CloudProvider.AWS;
        public Resource Resource { get; set; }
        public Resource ContainerImageBuildResource { get; set; }
        public Output<ImageDeploymentResult> DeploymentResult { get; }
        public IContainerRegistry Registry { get; }
    }

    private sealed class RecordingMocks : IMocks
    {
        private readonly ConcurrentQueue<MockResourceArgs> _resources = new();
        private readonly ConcurrentQueue<MockCallArgs> _calls = new();

        public IEnumerable<MockResourceArgs> CreatedResources(string type) =>
            _resources.Where(resource =>
                resource.Type == type && string.IsNullOrEmpty(resource.Id)
            );

        public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
        {
            _resources.Enqueue(args);
            var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
            var resourceName = args.Name ?? "resource";

            switch (args.Type)
            {
                case "aws:ecr/repository:Repository":
                    state["arn"] =
                        $"arn:aws:ecr:us-east-1:123456789012:repository/{resourceName}";
                    state["registryId"] = "123456789012";
                    state["repositoryUrl"] =
                        $"123456789012.dkr.ecr.us-east-1.amazonaws.com/{resourceName}";
                    break;
                case EcsClusterType:
                    state["arn"] =
                        $"arn:aws:ecs:us-east-1:123456789012:cluster/{resourceName}";
                    state["name"] = resourceName;
                    break;
                case "aws:iam/role:Role":
                    state["arn"] = $"arn:aws:iam::123456789012:role/{resourceName}";
                    state["name"] = resourceName;
                    break;
                case "aws:cloudwatch/logGroup:LogGroup":
                    state["arn"] =
                        $"arn:aws:logs:us-east-1:123456789012:log-group:{resourceName}";
                    state["name"] = resourceName;
                    break;
                case SecretsManagerSecretType:
                    state["arn"] =
                        $"arn:aws:secretsmanager:us-east-1:123456789012:secret:{resourceName}";
                    state["name"] = resourceName;
                    break;
                case SecretsManagerSecretVersionType:
                    state["arn"] =
                        $"arn:aws:secretsmanager:us-east-1:123456789012:secret:{resourceName}";
                    state["versionId"] = $"{resourceName}-version-id";
                    break;
                case EcsExpressGatewayServiceType:
                    state["serviceArn"] =
                        $"arn:aws:ecs:us-east-1:123456789012:service/{resourceName}";
                    state["ingressPaths"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["accessType"] = "PUBLIC",
                            ["endpoint"] = $"{resourceName}.example.test",
                        },
                    };
                    break;
                case EcsServiceType:
                    state["arn"] =
                        $"arn:aws:ecs:us-east-1:123456789012:service/{resourceName}";
                    state["name"] = args.Inputs.TryGetValue("name", out var serviceName)
                        ? serviceName
                        : resourceName;
                    break;
                case EcsTaskDefinitionType:
                    state["arn"] =
                        $"arn:aws:ecs:us-east-1:123456789012:task-definition/{resourceName}:1";
                    break;
                case LoadBalancerType:
                    state["arn"] =
                        $"arn:aws:elasticloadbalancing:us-east-1:123456789012:loadbalancer/app/{resourceName}/abc";
                    state["dnsName"] = $"{resourceName}-1234.us-east-1.elb.amazonaws.com";
                    break;
                case TargetGroupType:
                    state["arn"] =
                        $"arn:aws:elasticloadbalancing:us-east-1:123456789012:targetgroup/{resourceName}/abc";
                    break;
                case SecurityGroupType:
                    state["id"] = $"sg-{resourceName}";
                    break;
                case EksFargateProfileType:
                    state["arn"] =
                        $"arn:aws:eks:us-east-1:123456789012:fargateprofile/shared-eks/{resourceName}";
                    state["status"] = "ACTIVE";
                    break;
                case KubernetesServiceType:
                    state["status"] = new Dictionary<string, object>
                    {
                        ["loadBalancer"] = new Dictionary<string, object>
                        {
                            ["ingress"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["hostname"] = $"{resourceName}.example.test",
                                },
                            },
                        },
                    };
                    break;
            }

            return Task.FromResult<(string? id, object state)>(
                (string.IsNullOrEmpty(args.Id) ? $"{resourceName}-id" : args.Id, state)
            );
        }

        public Task<object> CallAsync(MockCallArgs args)
        {
            _calls.Enqueue(args);
            var token = args.Token ?? string.Empty;
            if (token.Contains("eks/getCluster", StringComparison.Ordinal))
            {
                return Task.FromResult<object>(
                    new Dictionary<string, object>
                    {
                        ["arn"] = "arn:aws:eks:us-east-1:123456789012:cluster/shared-eks",
                        ["name"] = "shared-eks",
                        ["endpoint"] = "https://shared-eks.example.test",
                        ["certificateAuthorities"] = new object[]
                        {
                            new Dictionary<string, object> { ["data"] = "dGVzdC1jYQ==" },
                        },
                    }
                );
            }
            if (token.Contains("ecr/getAuthorizationToken", StringComparison.Ordinal))
            {
                return Task.FromResult<object>(
                    new Dictionary<string, object>
                    {
                        ["authorizationToken"] = "dGVzdDp0ZXN0",
                        ["expiresAt"] = "2099-01-01T00:00:00Z",
                        ["password"] = "test",
                        ["proxyEndpoint"] = "https://123456789012.dkr.ecr.us-east-1.amazonaws.com",
                        ["registryId"] = "123456789012",
                        ["userName"] = "AWS",
                    }
                );
            }
            if (token.Contains("getRegion", StringComparison.Ordinal))
            {
                return Task.FromResult<object>(
                    new Dictionary<string, object> { ["region"] = "us-east-1" }
                );
            }
            if (token.Contains("ec2/getVpc", StringComparison.Ordinal))
            {
                return Task.FromResult<object>(
                    new Dictionary<string, object> { ["id"] = "vpc-default" }
                );
            }
            if (token.Contains("ec2/getSubnets", StringComparison.Ordinal))
            {
                return Task.FromResult<object>(
                    new Dictionary<string, object>
                    {
                        ["ids"] = new object[] { "subnet-default-a", "subnet-default-b" },
                    }
                );
            }
            if (token.Contains("ec2/getSubnet", StringComparison.Ordinal))
            {
                return Task.FromResult<object>(
                    new Dictionary<string, object> { ["vpcId"] = "vpc-supplied" }
                );
            }
            return Task.FromResult<object>(args.Args);
        }

        public void RegisterResourceOutputs(MockRegisterResourceOutputsRequest args) { }
    }
}
