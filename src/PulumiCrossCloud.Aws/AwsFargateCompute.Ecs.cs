using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

/// <summary>
/// The standard ECS backend. Where Express Mode hides ingress and networking behind one
/// resource, this builds the pieces explicitly — task definition, security groups, load
/// balancer, service, and autoscaling — so callers can reach the parts Express does not expose.
/// </summary>
public partial class AwsFargateCompute
{
    private const int DefaultEcsCpuUnits = 256;
    private const int DefaultEcsMemoryMebibytes = 512;
    private const int EcsHealthCheckGracePeriodSeconds = 60;

    private Pulumi.Aws.Ecs.Service? _ecsService;
    private Pulumi.Aws.Ecs.TaskDefinition? _ecsTaskDefinition;
    private Pulumi.Aws.LB.LoadBalancer? _ecsLoadBalancer;
    private Pulumi.Aws.LB.TargetGroup? _ecsTargetGroup;

    /// <summary>
    /// The VPC and subnets the service runs in. <paramref name="SubnetsDiscovered"/> records
    /// whether the subnets were chosen by this class rather than supplied by the caller, which
    /// decides the default for public IP assignment.
    /// </summary>
    private sealed record EcsNetwork(
        Output<string> VpcId,
        Output<ImmutableArray<string>> SubnetIds,
        bool SubnetsDiscovered
    );

    partial void BuildEcsService()
    {
        ValidateEcsConfiguration();

        var (cpuUnits, memoryMebibytes) = ResolveEcsTaskSize();
        var resourceOptions = new CustomResourceOptions { Provider = _provider };
        var network = ResolveEcsNetwork();
        var primaryPort = GetPrimaryPort();

        var foundation = CreateEcsFoundation(
            _options.EcsEnableExecuteCommand ? CreateEcsExecPolicyStatements() : null
        );
        var dependencies = new List<Resource>(foundation.Dependencies);

        var taskDefinitionArgs = new Pulumi.Aws.Ecs.TaskDefinitionArgs
        {
            Family = Name,
            NetworkMode = "awsvpc",
            RequiresCompatibilities = ["FARGATE"],
            Cpu = (cpuUnits ?? DefaultEcsCpuUnits).ToString(CultureInfo.InvariantCulture),
            Memory = (memoryMebibytes ?? DefaultEcsMemoryMebibytes).ToString(
                CultureInfo.InvariantCulture
            ),
            ExecutionRoleArn = foundation.ExecutionRole.Arn,
            ContainerDefinitions = CreateEcsContainerDefinitions(foundation.LogGroup),
            Tags = ToInputMap(_options.Tags),
        };
        if (foundation.TaskRole is { } taskRole)
            taskDefinitionArgs.TaskRoleArn = taskRole.Arn;

        _ecsTaskDefinition = new Pulumi.Aws.Ecs.TaskDefinition(
            $"{Name}-task",
            taskDefinitionArgs,
            new CustomResourceOptions { Provider = _provider, DependsOn = [.. dependencies] }
        );
        dependencies.Add(_ecsTaskDefinition);

        var loadBalancerSecurityGroup = _options.EcsCreateLoadBalancer
            ? CreateEcsLoadBalancerSecurityGroup(network, resourceOptions)
            : null;
        var serviceSecurityGroups = ResolveEcsServiceSecurityGroups(
            network,
            loadBalancerSecurityGroup,
            primaryPort,
            resourceOptions,
            dependencies
        );

        Pulumi.Aws.LB.Listener? listener = null;
        if (_options.EcsCreateLoadBalancer && loadBalancerSecurityGroup is not null)
        {
            listener = CreateEcsLoadBalancer(
                network,
                loadBalancerSecurityGroup,
                primaryPort,
                resourceOptions,
                dependencies
            );
        }

        if (_containerImage?.Resource is { } imageResource)
            dependencies.Add(imageResource);
        foreach (var (secret, _) in _requiredSecrets)
            dependencies.Add(secret.ValueResource);

        var autoscales = _options.EcsEnableAutoscaling && MaxCount > MinCount;

        var serviceArgs = new Pulumi.Aws.Ecs.ServiceArgs
        {
            Name = Name,
            Cluster = foundation.Cluster,
            TaskDefinition = _ecsTaskDefinition.Arn,
            DesiredCount = MinCount,
            LaunchType = "FARGATE",
            EnableExecuteCommand = _options.EcsEnableExecuteCommand,
            WaitForSteadyState = _options.WaitForSteadyState,
            NetworkConfiguration = new Pulumi.Aws.Ecs.Inputs.ServiceNetworkConfigurationArgs
            {
                Subnets = network.SubnetIds,
                SecurityGroups = serviceSecurityGroups,
                AssignPublicIp = _options.EcsAssignPublicIp ?? network.SubnetsDiscovered,
            },
            Tags = ToInputMap(_options.Tags),
        };

        if (_options.EcsEnableCircuitBreaker)
            serviceArgs.DeploymentCircuitBreaker =
                new Pulumi.Aws.Ecs.Inputs.ServiceDeploymentCircuitBreakerArgs
                {
                    Enable = true,
                    Rollback = true,
                };

        if (_ecsTargetGroup is not null)
        {
            serviceArgs.LoadBalancers =
            [
                new Pulumi.Aws.Ecs.Inputs.ServiceLoadBalancerArgs
                {
                    TargetGroupArn = _ecsTargetGroup.Arn,
                    ContainerName = Name,
                    ContainerPort = primaryPort,
                },
            ];
            serviceArgs.HealthCheckGracePeriodSeconds = EcsHealthCheckGracePeriodSeconds;
        }

        var serviceOptions = new CustomResourceOptions
        {
            Provider = _provider,
            DependsOn = listener is null ? [.. dependencies] : [.. dependencies, listener],
        };
        // Application Auto Scaling owns the desired count once a scaling policy exists, so
        // leaving it under Pulumi's management would reset the service on every update.
        if (autoscales)
            serviceOptions.IgnoreChanges = ["desiredCount"];

        _ecsService = new Pulumi.Aws.Ecs.Service($"{Name}-service", serviceArgs, serviceOptions);
        dependencies.Add(_ecsService);

        if (autoscales)
            dependencies.AddRange(CreateEcsAutoscaling(foundation.ClusterName, _ecsService));

        _arn = _ecsService.Arn;
        if (_ecsLoadBalancer is not null)
        {
            var scheme = _options.EcsCertificateArn is null ? "http" : "https";
            _dnsTarget = _ecsLoadBalancer.DnsName;
            _serviceUrl = _ecsLoadBalancer.DnsName.Apply(dns =>
                string.IsNullOrWhiteSpace(dns) ? string.Empty : $"{scheme}://{dns}"
            );
        }

        _dependencies.AddRange(dependencies);
    }

    private void ValidateEcsConfiguration()
    {
        if (!_options.EcsCreateLoadBalancer)
            return;

        if (_options.SubnetIds is { Length: > 0 and < 2 })
            throw new ArgumentException(
                "An Application Load Balancer requires at least two subnet IDs in different availability zones. "
                    + "Supply more subnets, or set AwsFargateOptions.EcsCreateLoadBalancer to false.",
                nameof(_options.SubnetIds)
            );

        var protocol = PortForwardings.FirstOrDefault().Protocol;
        if (
            !string.IsNullOrWhiteSpace(protocol)
            && !string.Equals(protocol, "TCP", StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidOperationException(
                $"An Application Load Balancer cannot forward to the '{protocol}' primary container port. "
                    + "Use a TCP port, or set AwsFargateOptions.EcsCreateLoadBalancer to false."
            );

        if (
            HealthCheckConfiguration is { } healthCheck
            && healthCheck.TimeoutSeconds >= healthCheck.IntervalSeconds
        )
            throw new InvalidOperationException(
                $"The load balancer health check timeout ({healthCheck.TimeoutSeconds}s) must be less than "
                    + $"its interval ({healthCheck.IntervalSeconds}s)."
            );
    }

    private EcsNetwork ResolveEcsNetwork()
    {
        var invokeOptions = new InvokeOutputOptions { Provider = _provider };

        if (_options.SubnetIds is { Length: > 0 } configuredSubnets)
        {
            var subnetVpcId = _options.VpcId is { } explicitVpcId
                ? Output.Create(explicitVpcId)
                : Pulumi
                    .Aws.Ec2.GetSubnet.Invoke(
                        new Pulumi.Aws.Ec2.GetSubnetInvokeArgs { Id = configuredSubnets[0] },
                        invokeOptions
                    )
                    .Apply(subnet => subnet.VpcId);

            return new EcsNetwork(
                subnetVpcId,
                Output.Create(configuredSubnets.ToImmutableArray()),
                SubnetsDiscovered: false
            );
        }

        var vpcId = _options.VpcId is { } configuredVpcId
            ? Output.Create(configuredVpcId)
            : Pulumi
                .Aws.Ec2.GetVpc.Invoke(
                    new Pulumi.Aws.Ec2.GetVpcInvokeArgs { Default = true },
                    invokeOptions
                )
                .Apply(vpc => vpc.Id);

        var subnetIds = Pulumi
            .Aws.Ec2.GetSubnets.Invoke(
                new Pulumi.Aws.Ec2.GetSubnetsInvokeArgs
                {
                    Filters =
                    [
                        new Pulumi.Aws.Ec2.Inputs.GetSubnetsFilterInputArgs
                        {
                            Name = "vpc-id",
                            Values = new InputList<string> { vpcId },
                        },
                    ],
                },
                invokeOptions
            )
            .Apply(subnets => subnets.Ids);

        return new EcsNetwork(vpcId, subnetIds, SubnetsDiscovered: true);
    }

    private Pulumi.Aws.Ec2.SecurityGroup CreateEcsLoadBalancerSecurityGroup(
        EcsNetwork network,
        CustomResourceOptions resourceOptions
    )
    {
        var ingress = new List<Pulumi.Aws.Ec2.Inputs.SecurityGroupIngressArgs>
        {
            new()
            {
                Protocol = "tcp",
                FromPort = 80,
                ToPort = 80,
                CidrBlocks = ["0.0.0.0/0"],
                Description = "HTTP from the internet",
            },
        };
        if (_options.EcsCertificateArn is not null)
            ingress.Add(
                new Pulumi.Aws.Ec2.Inputs.SecurityGroupIngressArgs
                {
                    Protocol = "tcp",
                    FromPort = 443,
                    ToPort = 443,
                    CidrBlocks = ["0.0.0.0/0"],
                    Description = "HTTPS from the internet",
                }
            );

        return new Pulumi.Aws.Ec2.SecurityGroup(
            $"{Name}-lb-sg",
            new Pulumi.Aws.Ec2.SecurityGroupArgs
            {
                VpcId = network.VpcId,
                Description = $"Load balancer ingress for {Name}",
                Ingress = ingress,
                Egress =
                [
                    new Pulumi.Aws.Ec2.Inputs.SecurityGroupEgressArgs
                    {
                        Protocol = "-1",
                        FromPort = 0,
                        ToPort = 0,
                        CidrBlocks = ["0.0.0.0/0"],
                        Description = "Allow all outbound",
                    },
                ],
                Tags = ToInputMap(_options.Tags),
            },
            resourceOptions
        );
    }

    /// <summary>
    /// Returns the security groups attached to the service's tasks. Caller-supplied groups are
    /// used as-is: they belong to the caller, so this does not add ingress rules to them and the
    /// caller is responsible for allowing traffic from the load balancer.
    /// </summary>
    private InputList<string> ResolveEcsServiceSecurityGroups(
        EcsNetwork network,
        Pulumi.Aws.Ec2.SecurityGroup? loadBalancerSecurityGroup,
        int primaryPort,
        CustomResourceOptions resourceOptions,
        List<Resource> dependencies
    )
    {
        if (_options.SecurityGroupIds is { Length: > 0 } configuredSecurityGroups)
            return configuredSecurityGroups;

        var ingress = new List<Pulumi.Aws.Ec2.Inputs.SecurityGroupIngressArgs>();
        if (loadBalancerSecurityGroup is not null)
            ingress.Add(
                new Pulumi.Aws.Ec2.Inputs.SecurityGroupIngressArgs
                {
                    Protocol = "tcp",
                    FromPort = primaryPort,
                    ToPort = primaryPort,
                    SecurityGroups = [loadBalancerSecurityGroup.Id],
                    Description = "Load balancer to task",
                }
            );

        var securityGroup = new Pulumi.Aws.Ec2.SecurityGroup(
            $"{Name}-service-sg",
            new Pulumi.Aws.Ec2.SecurityGroupArgs
            {
                VpcId = network.VpcId,
                Description = $"Task networking for {Name}",
                Ingress = ingress,
                Egress =
                [
                    new Pulumi.Aws.Ec2.Inputs.SecurityGroupEgressArgs
                    {
                        Protocol = "-1",
                        FromPort = 0,
                        ToPort = 0,
                        CidrBlocks = ["0.0.0.0/0"],
                        Description = "Allow all outbound",
                    },
                ],
                Tags = ToInputMap(_options.Tags),
            },
            resourceOptions
        );
        dependencies.Add(securityGroup);

        return new InputList<string> { securityGroup.Id };
    }

    /// <summary>
    /// Creates the load balancer, target group, and listeners, returning the listener the
    /// service must wait for before registering targets.
    /// </summary>
    private Pulumi.Aws.LB.Listener CreateEcsLoadBalancer(
        EcsNetwork network,
        Pulumi.Aws.Ec2.SecurityGroup loadBalancerSecurityGroup,
        int primaryPort,
        CustomResourceOptions resourceOptions,
        List<Resource> dependencies
    )
    {
        _ecsLoadBalancer = new Pulumi.Aws.LB.LoadBalancer(
            $"{Name}-alb",
            new Pulumi.Aws.LB.LoadBalancerArgs
            {
                LoadBalancerType = "application",
                Internal = _options.EcsInternalLoadBalancer,
                SecurityGroups = [loadBalancerSecurityGroup.Id],
                Subnets = network.SubnetIds,
                Tags = ToInputMap(_options.Tags),
            },
            resourceOptions
        );
        dependencies.Add(loadBalancerSecurityGroup);
        dependencies.Add(_ecsLoadBalancer);

        _ecsTargetGroup = new Pulumi.Aws.LB.TargetGroup(
            $"{Name}-tg",
            new Pulumi.Aws.LB.TargetGroupArgs
            {
                Port = primaryPort,
                Protocol = "HTTP",
                TargetType = "ip",
                VpcId = network.VpcId,
                HealthCheck = new Pulumi.Aws.LB.Inputs.TargetGroupHealthCheckArgs
                {
                    Enabled = true,
                    Path = NormalizeHealthPath(HealthCheckConfiguration?.Path),
                    Protocol = "HTTP",
                    Matcher = "200-399",
                    Interval = HealthCheckConfiguration?.IntervalSeconds ?? 30,
                    Timeout = HealthCheckConfiguration?.TimeoutSeconds ?? 5,
                    HealthyThreshold = HealthCheckConfiguration?.HealthyThreshold ?? 3,
                    UnhealthyThreshold = HealthCheckConfiguration?.UnhealthyThreshold ?? 3,
                },
                Tags = ToInputMap(_options.Tags),
            },
            resourceOptions
        );
        dependencies.Add(_ecsTargetGroup);

        var forward = new Pulumi.Aws.LB.Inputs.ListenerDefaultActionArgs
        {
            Type = "forward",
            TargetGroupArn = _ecsTargetGroup.Arn,
        };

        if (_options.EcsCertificateArn is not { } certificateArn)
        {
            return new Pulumi.Aws.LB.Listener(
                $"{Name}-listener",
                new Pulumi.Aws.LB.ListenerArgs
                {
                    LoadBalancerArn = _ecsLoadBalancer.Arn,
                    Port = 80,
                    Protocol = "HTTP",
                    DefaultActions = [forward],
                    Tags = ToInputMap(_options.Tags),
                },
                resourceOptions
            );
        }

        var httpsListener = new Pulumi.Aws.LB.Listener(
            $"{Name}-https-listener",
            new Pulumi.Aws.LB.ListenerArgs
            {
                LoadBalancerArn = _ecsLoadBalancer.Arn,
                Port = 443,
                Protocol = "HTTPS",
                CertificateArn = certificateArn,
                DefaultActions = [forward],
                Tags = ToInputMap(_options.Tags),
            },
            resourceOptions
        );

        dependencies.Add(
            new Pulumi.Aws.LB.Listener(
                $"{Name}-listener",
                new Pulumi.Aws.LB.ListenerArgs
                {
                    LoadBalancerArn = _ecsLoadBalancer.Arn,
                    Port = 80,
                    Protocol = "HTTP",
                    DefaultActions =
                    [
                        new Pulumi.Aws.LB.Inputs.ListenerDefaultActionArgs
                        {
                            Type = "redirect",
                            Redirect =
                                new Pulumi.Aws.LB.Inputs.ListenerDefaultActionRedirectArgs
                                {
                                    Port = "443",
                                    Protocol = "HTTPS",
                                    StatusCode = "HTTP_301",
                                },
                        },
                    ],
                    Tags = ToInputMap(_options.Tags),
                },
                resourceOptions
            )
        );

        return httpsListener;
    }

    private List<Resource> CreateEcsAutoscaling(
        Output<string> clusterName,
        Pulumi.Aws.Ecs.Service service
    )
    {
        var resourceOptions = new CustomResourceOptions
        {
            Provider = _provider,
            DependsOn = [service],
        };

        var target = new Pulumi.Aws.AppAutoScaling.Target(
            $"{Name}-autoscaling-target",
            new Pulumi.Aws.AppAutoScaling.TargetArgs
            {
                MinCapacity = MinCount,
                MaxCapacity = MaxCount,
                ResourceId = Output
                    .Tuple(clusterName, service.Name)
                    .Apply(names => $"service/{names.Item1}/{names.Item2}"),
                ScalableDimension = "ecs:service:DesiredCount",
                ServiceNamespace = "ecs",
            },
            resourceOptions
        );

        var policy = new Pulumi.Aws.AppAutoScaling.Policy(
            $"{Name}-autoscaling-policy",
            new Pulumi.Aws.AppAutoScaling.PolicyArgs
            {
                PolicyType = "TargetTrackingScaling",
                ResourceId = target.ResourceId,
                ScalableDimension = target.ScalableDimension,
                ServiceNamespace = target.ServiceNamespace,
                TargetTrackingScalingPolicyConfiguration =
                    new Pulumi.Aws.AppAutoScaling.Inputs.PolicyTargetTrackingScalingPolicyConfigurationArgs
                    {
                        TargetValue = 60,
                        PredefinedMetricSpecification =
                            new Pulumi.Aws.AppAutoScaling.Inputs.PolicyTargetTrackingScalingPolicyConfigurationPredefinedMetricSpecificationArgs
                            {
                                PredefinedMetricType = "ECSServiceAverageCPUUtilization",
                            },
                    },
            },
            new CustomResourceOptions { Provider = _provider, DependsOn = [target] }
        );

        return [target, policy];
    }

    /// <summary>
    /// Renders the task's container definitions. Image, log group, output-valued environment
    /// variables, and secret ARNs are all unresolved at construction time, so they are gathered
    /// into one <see cref="Output"/> and read back positionally once resolved.
    /// </summary>
    private Output<string> CreateEcsContainerDefinitions(
        Pulumi.Aws.CloudWatch.LogGroup logGroup
    )
    {
        var region = Pulumi
            .Aws.GetRegion.Invoke(
                new Pulumi.Aws.GetRegionInvokeArgs(),
                new InvokeOutputOptions { Provider = _provider }
            )
            .Apply(result => result.Region);

        var outputEnvironment = _environmentVariablesOutput.ToList();
        var secretItems = _requiredSecrets.ToList();

        var unresolved = new List<Output<string>>
        {
            GetContainerImageUri(),
            logGroup.Name,
            region,
        };
        unresolved.AddRange(outputEnvironment.Select(item => item.Value));
        unresolved.AddRange(secretItems.Select(item => item.Secret.Arn));

        return Output.All([.. unresolved]).Apply(resolved =>
        {
            var index = 0;
            var image = resolved[index++];
            var logGroupName = resolved[index++];
            var regionName = resolved[index++];

            var environment = new List<object>();
            foreach (var (name, value) in _environmentVariables)
                environment.Add(new { name, value });
            foreach (var (name, _) in outputEnvironment)
                environment.Add(new { name, value = resolved[index++] });

            // Read eagerly: every value below is positional, so a deferred LINQ pipeline would
            // advance the shared index out of order.
            var secrets = new List<object>();
            foreach (var item in secretItems)
                secrets.Add(
                    new
                    {
                        name = GetSecretEnvironmentVariableName(item),
                        valueFrom = resolved[index++],
                    }
                );

            var portMappings = PortForwardings
                .Select(
                    (port, position) =>
                        new
                        {
                            name = position == 0 ? "http" : $"port-{position + 1}",
                            containerPort = port.ContainerPort,
                            protocol = NormalizeEcsProtocol(port.Protocol),
                        }
                )
                .ToList();

            var container = new Dictionary<string, object?>
            {
                ["name"] = Name,
                ["image"] = image,
                ["essential"] = true,
                ["portMappings"] = portMappings,
                ["environment"] = environment,
                ["secrets"] = secrets,
                ["logConfiguration"] = new
                {
                    logDriver = "awslogs",
                    options = new Dictionary<string, string>
                    {
                        ["awslogs-group"] = logGroupName,
                        ["awslogs-region"] = regionName,
                        ["awslogs-stream-prefix"] = Name,
                    },
                },
            };

            return JsonSerializer.Serialize(new[] { container });
        });
    }

    private static string NormalizeEcsProtocol(string? protocol)
    {
        var normalized = string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.ToLowerInvariant();
        if (normalized is not ("tcp" or "udp"))
            throw new InvalidOperationException(
                $"ECS port mappings support the tcp and udp protocols, not '{protocol}'."
            );
        return normalized;
    }

    /// <summary>
    /// The channel permissions the ECS Exec agent needs on the task role. Without these the
    /// service still runs, but every exec session fails to connect.
    /// </summary>
    private static List<IPolicyStatement> CreateEcsExecPolicyStatements() =>
        [
            new AwsPolicyStatement
            {
                Effect = "Allow",
                Actions =
                [
                    "ssmmessages:CreateControlChannel",
                    "ssmmessages:CreateDataChannel",
                    "ssmmessages:OpenControlChannel",
                    "ssmmessages:OpenDataChannel",
                ],
                Resources = [Output.Create("*")],
            },
        ];

    partial void AddEcsPolicy(List<IPolicyStatement> statements, ComputePermissions permission)
    {
        switch (permission)
        {
            case ComputePermissions.GetStatus:
                AddServiceArnPolicyStatement(statements, "ecs:DescribeServices");
                break;
            case ComputePermissions.Start:
            case ComputePermissions.Stop:
            case ComputePermissions.Restart:
            case ComputePermissions.Scale:
            case ComputePermissions.UpdateConfiguration:
                AddServiceArnPolicyStatement(statements, "ecs:UpdateService");
                break;
            case ComputePermissions.Terminate:
                AddServiceArnPolicyStatement(statements, "ecs:DeleteService");
                break;
            case ComputePermissions.Connect:
                // ECS Exec authorizes the task and cluster, rather than the service.
                var prefix = Arn.Apply(arn => string.Join(':', arn.Split(':').Take(5)));
                var clusterName =
                    _ecsCluster?.Name
                    ?? Output.Create(
                        GetClusterShortName(_options.ClusterName ?? $"{Name}-cluster")
                    );
                statements.Add(
                    new AwsPolicyStatement
                    {
                        Effect = "Allow",
                        Actions = ["ecs:ExecuteCommand"],
                        Resources =
                        [
                            Output.Format($"{prefix}:cluster/{clusterName}"),
                            Output.Format($"{prefix}:task/{clusterName}/*"),
                        ],
                        Conditions = new Dictionary<string, object>
                        {
                            ["StringEquals"] = new Dictionary<string, string>
                            {
                                ["ecs:container-name"] = Name,
                            },
                        },
                    }
                );
                break;
            case ComputePermissions.GetLogs:
                statements.Add(CreateEcsLogsPolicyStatement());
                break;
        }
    }
}
