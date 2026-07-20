using System.Globalization;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

/// <summary>
/// The ECS Express Mode backend. Express provisions ingress, scaling, and networking from a
/// single service resource, which is why it needs far less configuration than the standard ECS
/// backend in <c>AwsFargateCompute.Ecs.cs</c>.
/// </summary>
public partial class AwsFargateCompute
{
    private const string EcsExpressInfrastructurePolicyArn =
        "arn:aws:iam::aws:policy/service-role/AmazonECSInfrastructureRoleforExpressGatewayServices";

    private Pulumi.Aws.Ecs.ExpressGatewayService? _ecsExpressService;
    private Pulumi.Aws.Iam.Role? _infrastructureRole;

    partial void BuildEcsExpressService()
    {
        if (_options.SubnetIds is { Length: 1 })
            throw new ArgumentException(
                "ECS Express Mode requires at least two subnet IDs when custom networking is configured.",
                nameof(_options.SubnetIds)
            );

        var (cpuUnits, memoryMebibytes) = ResolveEcsTaskSize();
        var resourceOptions = new CustomResourceOptions { Provider = _provider };
        var foundation = CreateEcsFoundation();

        _infrastructureRole = CreateRole($"{Name}-infrastructure-role", "ecs.amazonaws.com");
        var infrastructureAttachment = new Pulumi.Aws.Iam.RolePolicyAttachment(
            $"{Name}-infrastructure-managed-policy",
            new Pulumi.Aws.Iam.RolePolicyAttachmentArgs
            {
                Role = _infrastructureRole.Name,
                PolicyArn = EcsExpressInfrastructurePolicyArn,
            },
            resourceOptions
        );

        var primaryContainer = CreateEcsExpressPrimaryContainer(foundation.LogGroup);
        var serviceArgs = new Pulumi.Aws.Ecs.ExpressGatewayServiceArgs
        {
            Cluster = foundation.Cluster,
            ExecutionRoleArn = foundation.ExecutionRole.Arn,
            InfrastructureRoleArn = _infrastructureRole.Arn,
            PrimaryContainer = primaryContainer,
            ServiceName = Name,
            HealthCheckPath = NormalizeHealthPath(HealthCheckConfiguration?.Path),
            ScalingTargets =
            [
                new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServiceScalingTargetArgs
                {
                    AutoScalingMetric = "AVERAGE_CPU",
                    AutoScalingTargetValue = 60,
                    MinTaskCount = MinCount,
                    MaxTaskCount = MaxCount,
                },
            ],
            Tags = ToInputMap(_options.Tags),
            WaitForSteadyState = _options.WaitForSteadyState,
        };

        if (foundation.TaskRole is not null)
            serviceArgs.TaskRoleArn = foundation.TaskRole.Arn;
        if (cpuUnits is { } resolvedCpuUnits)
            serviceArgs.Cpu = resolvedCpuUnits.ToString(CultureInfo.InvariantCulture);
        if (memoryMebibytes is { } resolvedMemoryMebibytes)
            serviceArgs.Memory = resolvedMemoryMebibytes.ToString(CultureInfo.InvariantCulture);
        if (_options.SubnetIds is { Length: > 0 } || _options.SecurityGroupIds is { Length: > 0 })
        {
            var networkConfiguration =
                new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServiceNetworkConfigurationArgs();
            if (_options.SubnetIds is { Length: > 0 })
                networkConfiguration.Subnets = _options.SubnetIds;
            if (_options.SecurityGroupIds is { Length: > 0 })
                networkConfiguration.SecurityGroups = _options.SecurityGroupIds;
            serviceArgs.NetworkConfigurations = [networkConfiguration];
        }

        var serviceDependencies = new List<Resource>(foundation.Dependencies)
        {
            _infrastructureRole,
            infrastructureAttachment,
        };
        if (_containerImage?.Resource is { } imageResource)
            serviceDependencies.Add(imageResource);
        foreach (var (secret, _) in _requiredSecrets)
            serviceDependencies.Add(secret.ValueResource);

        _ecsExpressService = new Pulumi.Aws.Ecs.ExpressGatewayService(
            $"{Name}-service",
            serviceArgs,
            new CustomResourceOptions
            {
                Provider = _provider,
                DependsOn = serviceDependencies,
            }
        );

        _arn = _ecsExpressService.ServiceArn;
        var endpoint = _ecsExpressService.IngressPaths.Apply(paths =>
            paths.FirstOrDefault(path =>
                    string.Equals(path.AccessType, "PUBLIC", StringComparison.OrdinalIgnoreCase)
                )?.Endpoint
                ?? paths.FirstOrDefault()?.Endpoint
                ?? string.Empty
        );
        _serviceUrl = endpoint.Apply(EnsureUrlScheme);
        _dnsTarget = endpoint.Apply(GetDnsTarget);

        _dependencies.AddRange(serviceDependencies);
        _dependencies.Add(_ecsExpressService);
    }

    private Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerArgs CreateEcsExpressPrimaryContainer(
        Pulumi.Aws.CloudWatch.LogGroup logGroup
    )
    {
        var containerPort = GetEcsExpressPrimaryPort();
        var environments = new List<
            Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerEnvironmentArgs
        >();
        foreach (var (name, value) in _environmentVariables)
            environments.Add(new() { Name = name, Value = value });
        foreach (var (name, value) in _environmentVariablesOutput)
            environments.Add(new() { Name = name, Value = value });

        var secrets = _requiredSecrets
            .Select(item =>
                new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerSecretArgs
                {
                    Name = GetSecretEnvironmentVariableName(item),
                    ValueFrom = item.Secret.Arn,
                }
            )
            .ToList();

        return new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerArgs
        {
            Image = GetContainerImageUri(),
            ContainerPort = containerPort,
            Environments = environments,
            Secrets = secrets,
            AwsLogsConfigurations =
            [
                new Pulumi.Aws.Ecs.Inputs.ExpressGatewayServicePrimaryContainerAwsLogsConfigurationArgs
                {
                    LogGroup = logGroup.Name,
                    LogStreamPrefix = Name,
                },
            ],
        };
    }

    /// <summary>
    /// Resolves the primary container port, rejecting protocols Express Mode cannot expose.
    /// Express only offers TCP/HTTP ingress, so a non-TCP primary port could never be reached.
    /// </summary>
    private int GetEcsExpressPrimaryPort()
    {
        var protocol = PortForwardings.FirstOrDefault().Protocol;
        if (
            !string.IsNullOrWhiteSpace(protocol)
            && !string.Equals(protocol, "TCP", StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidOperationException(
                "ECS Express Mode supports TCP/HTTP ingress for the primary container port."
            );

        return GetPrimaryPort();
    }

    partial void AddEcsExpressPolicy(
        List<IPolicyStatement> statements,
        ComputePermissions permission
    )
    {
        switch (permission)
        {
            case ComputePermissions.GetStatus:
                AddServiceArnPolicyStatement(statements, "ecs:DescribeExpressGatewayService");
                break;
            case ComputePermissions.Start:
            case ComputePermissions.Stop:
            case ComputePermissions.Restart:
            case ComputePermissions.Scale:
                AddServiceArnPolicyStatement(statements, "ecs:UpdateExpressGatewayService");
                break;
            case ComputePermissions.Terminate:
                AddServiceArnPolicyStatement(statements, "ecs:DeleteExpressGatewayService");
                break;
            case ComputePermissions.Connect:
                // Express Gateway services do not expose ECS Exec enablement.
                break;
            case ComputePermissions.UpdateConfiguration:
                AddServiceArnPolicyStatement(statements, "ecs:UpdateExpressGatewayService");
                break;
            case ComputePermissions.GetLogs:
                statements.Add(CreateEcsLogsPolicyStatement());
                break;
        }
    }
}
