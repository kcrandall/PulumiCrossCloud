using System.Text.Json;
using System.Text.RegularExpressions;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public partial class AwsFargateCompute
{
    private const string EksFargatePodExecutionPolicyArn =
        "arn:aws:iam::aws:policy/AmazonEKSFargatePodExecutionRolePolicy";

    private Output<string> _eksClusterArn = Output.Create(string.Empty);
    private Output<string> _eksFargateProfileArn = Output.Create(string.Empty);

    partial void BuildEksService()
    {
        if (string.IsNullOrWhiteSpace(_options.ClusterName))
            throw new ArgumentException(
                "The EKS backend requires AwsFargateOptions.ClusterName for an existing Fargate-ready cluster.",
                nameof(_options.ClusterName)
            );
        if (_options.SubnetIds is not { Length: > 0 })
            throw new ArgumentException(
                "The EKS backend requires private subnet IDs in AwsFargateOptions.SubnetIds.",
                nameof(_options.SubnetIds)
            );
        if (_options.SecurityGroupIds is { Length: > 0 })
            throw new ArgumentException(
                "AwsFargateOptions.SecurityGroupIds is supported only by the ECS backend. Configure EKS security groups for pods on the existing cluster.",
                nameof(_options.SecurityGroupIds)
            );
        if (!Enum.IsDefined(_options.EksLoadBalancerMode))
            throw new ArgumentOutOfRangeException(
                nameof(_options.EksLoadBalancerMode),
                _options.EksLoadBalancerMode,
                "Unsupported EKS load balancer mode."
            );
        if (_customPolicyStatements.Count > 0)
            throw new NotSupportedException(
                "Custom AWS workload policies on EKS Fargate require IRSA. Configure IRSA on the existing cluster and workload separately."
            );

        var clusterName = _options.ClusterName;
        var namespaceName = GetKubernetesName(_options.KubernetesNamespace ?? Name);
        var workloadName = GetKubernetesName(Name);
        var labels = new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = workloadName,
            ["pulumi-cross-cloud/service"] = workloadName,
        };
        var awsResourceOptions = new CustomResourceOptions { Provider = _provider };
        var invokeOptions = new InvokeOutputOptions { Provider = _provider };
        var eksCluster = Pulumi.Aws.Eks.GetCluster.Invoke(
            new Pulumi.Aws.Eks.GetClusterInvokeArgs { Name = clusterName },
            invokeOptions
        );
        _eksClusterArn = eksCluster.Apply(cluster => cluster.Arn);

        var podExecutionRole = CreateRole(
            $"{Name}-eks-pod-execution-role",
            "eks-fargate-pods.amazonaws.com"
        );
        var podExecutionAttachment = new Pulumi.Aws.Iam.RolePolicyAttachment(
            $"{Name}-eks-pod-execution-policy",
            new Pulumi.Aws.Iam.RolePolicyAttachmentArgs
            {
                Role = podExecutionRole.Name,
                PolicyArn = EksFargatePodExecutionPolicyArn,
            },
            awsResourceOptions
        );

        var fargateProfile = new Pulumi.Aws.Eks.FargateProfile(
            $"{Name}-fargate-profile",
            new Pulumi.Aws.Eks.FargateProfileArgs
            {
                ClusterName = clusterName,
                FargateProfileName = GetKubernetesName($"{workloadName}-profile"),
                PodExecutionRoleArn = podExecutionRole.Arn,
                SubnetIds = _options.SubnetIds,
                Selectors =
                [
                    new Pulumi.Aws.Eks.Inputs.FargateProfileSelectorArgs
                    {
                        Namespace = namespaceName,
                        Labels = labels,
                    },
                ],
                Tags = ToInputMap(_options.Tags),
            },
            new CustomResourceOptions
            {
                Provider = _provider,
                DependsOn = [podExecutionAttachment],
            }
        );

        Input<string> kubeconfig =
            _options.Kubeconfig
            ?? CreateEksKubeconfig(clusterName, eksCluster, invokeOptions);
        var kubernetesProvider = new Pulumi.Kubernetes.Provider(
            $"{Name}-kubernetes-provider",
            new Pulumi.Kubernetes.ProviderArgs
            {
                KubeConfig = kubeconfig,
                ClusterIdentifier = clusterName,
                Namespace = namespaceName,
            }
        );
        var kubernetesResourceOptions = new CustomResourceOptions
        {
            Provider = kubernetesProvider,
            DependsOn = [fargateProfile],
        };

        Pulumi.Kubernetes.Core.V1.Namespace? kubernetesNamespace = null;
        if (_options.EksCreateNamespace)
        {
            kubernetesNamespace = new Pulumi.Kubernetes.Core.V1.Namespace(
                $"{Name}-namespace",
                new Pulumi.Kubernetes.Types.Inputs.Core.V1.NamespaceArgs
                {
                    Metadata = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs
                    {
                        Name = namespaceName,
                        Labels = labels,
                        Annotations = CreateEksAwaitAnnotations(),
                    },
                },
                kubernetesResourceOptions
            );
        }

        Pulumi.Kubernetes.Core.V1.Secret? kubernetesSecret = null;
        var secretName = GetKubernetesName($"{workloadName}-secrets");
        if (_requiredSecrets.Count > 0)
        {
            var stringData = new InputMap<string>();
            foreach (var item in _requiredSecrets)
                stringData.Add(
                    GetSecretEnvironmentVariableName(item),
                    item.Secret.Value.Apply(value => value ?? string.Empty)
                );

            var secretDependencies = new List<Resource> { fargateProfile };
            if (kubernetesNamespace is not null)
                secretDependencies.Add(kubernetesNamespace);

            kubernetesSecret = new Pulumi.Kubernetes.Core.V1.Secret(
                $"{Name}-secrets",
                new Pulumi.Kubernetes.Types.Inputs.Core.V1.SecretArgs
                {
                    Metadata = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs
                    {
                        Name = secretName,
                        Namespace = namespaceName,
                        Labels = labels,
                    },
                    StringData = stringData,
                    Type = "Opaque",
                },
                new CustomResourceOptions
                {
                    Provider = kubernetesProvider,
                    DependsOn = secretDependencies,
                }
            );
        }

        var container = CreateEksContainer(workloadName, secretName);
        var deploymentDependencies = new List<Resource>
        {
            fargateProfile,
            ContainerRegistry.Resource,
        };
        if (kubernetesNamespace is not null)
            deploymentDependencies.Add(kubernetesNamespace);
        if (kubernetesSecret is not null)
            deploymentDependencies.Add(kubernetesSecret);
        if (_containerImage?.Resource is { } imageResource)
            deploymentDependencies.Add(imageResource);

        var deployment = new Pulumi.Kubernetes.Apps.V1.Deployment(
            $"{Name}-deployment",
            new Pulumi.Kubernetes.Types.Inputs.Apps.V1.DeploymentArgs
            {
                Metadata = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs
                {
                    Name = workloadName,
                    Namespace = namespaceName,
                    Labels = labels,
                    Annotations = CreateEksAwaitAnnotations(),
                },
                Spec = new Pulumi.Kubernetes.Types.Inputs.Apps.V1.DeploymentSpecArgs
                {
                    Replicas = MinCount,
                    Selector = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.LabelSelectorArgs
                    {
                        MatchLabels = labels,
                    },
                    Template = new Pulumi.Kubernetes.Types.Inputs.Core.V1.PodTemplateSpecArgs
                    {
                        Metadata = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs
                        {
                            Labels = labels,
                        },
                        Spec = new Pulumi.Kubernetes.Types.Inputs.Core.V1.PodSpecArgs
                        {
                            Containers = [container],
                        },
                    },
                },
            },
            new CustomResourceOptions
            {
                Provider = kubernetesProvider,
                DependsOn = deploymentDependencies,
            }
        );

        Pulumi.Kubernetes.Autoscaling.V2.HorizontalPodAutoscaler? autoscaler = null;
        if (_options.EksEnableAutoscaling && MaxCount > MinCount && MinCount > 0)
        {
            autoscaler = new Pulumi.Kubernetes.Autoscaling.V2.HorizontalPodAutoscaler(
                $"{Name}-autoscaler",
                new Pulumi.Kubernetes.Types.Inputs.Autoscaling.V2.HorizontalPodAutoscalerArgs
                {
                    Metadata = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs
                    {
                        Name = GetKubernetesName($"{workloadName}-autoscaler"),
                        Namespace = namespaceName,
                        Labels = labels,
                    },
                    Spec =
                        new Pulumi.Kubernetes.Types.Inputs.Autoscaling.V2.HorizontalPodAutoscalerSpecArgs
                        {
                            MinReplicas = MinCount,
                            MaxReplicas = MaxCount,
                            ScaleTargetRef =
                                new Pulumi.Kubernetes.Types.Inputs.Autoscaling.V2.CrossVersionObjectReferenceArgs
                                {
                                    ApiVersion = "apps/v1",
                                    Kind = "Deployment",
                                    Name = workloadName,
                                },
                            Metrics =
                            [
                                new Pulumi.Kubernetes.Types.Inputs.Autoscaling.V2.MetricSpecArgs
                                {
                                    Type = "Resource",
                                    Resource =
                                        new Pulumi.Kubernetes.Types.Inputs.Autoscaling.V2.ResourceMetricSourceArgs
                                        {
                                            Name = "cpu",
                                            Target =
                                                new Pulumi.Kubernetes.Types.Inputs.Autoscaling.V2.MetricTargetArgs
                                                {
                                                    Type = "Utilization",
                                                    AverageUtilization = 60,
                                                },
                                        },
                                },
                            ],
                        },
                },
                new CustomResourceOptions
                {
                    Provider = kubernetesProvider,
                    DependsOn = [deployment],
                }
            );
        }

        var serviceAnnotations = new Dictionary<string, string>
        {
            ["service.beta.kubernetes.io/aws-load-balancer-nlb-target-type"] = "ip",
            ["service.beta.kubernetes.io/aws-load-balancer-scheme"] = "internet-facing",
        };
        if (!_options.WaitForSteadyState)
            serviceAnnotations["pulumi.com/skipAwait"] = "true";

        var serviceSpec = new Pulumi.Kubernetes.Types.Inputs.Core.V1.ServiceSpecArgs
        {
            Type = "LoadBalancer",
            Selector = labels,
            Ports = CreateEksServicePorts(),
        };
        switch (_options.EksLoadBalancerMode)
        {
            case AwsEksLoadBalancerMode.AwsLoadBalancerController:
                serviceAnnotations["service.beta.kubernetes.io/aws-load-balancer-type"] =
                    "external";
                break;
            case AwsEksLoadBalancerMode.AutoMode:
                serviceSpec.LoadBalancerClass = "eks.amazonaws.com/nlb";
                break;
        }

        var service = new Pulumi.Kubernetes.Core.V1.Service(
            $"{Name}-service",
            new Pulumi.Kubernetes.Types.Inputs.Core.V1.ServiceArgs
            {
                Metadata = new Pulumi.Kubernetes.Types.Inputs.Meta.V1.ObjectMetaArgs
                {
                    Name = workloadName,
                    Namespace = namespaceName,
                    Labels = labels,
                    Annotations = serviceAnnotations,
                },
                Spec = serviceSpec,
            },
            new CustomResourceOptions
            {
                Provider = kubernetesProvider,
                DependsOn = [deployment, fargateProfile],
            }
        );

        var endpoint = service.Status.Apply(status =>
            status.LoadBalancer?.Ingress.FirstOrDefault()?.Hostname
                ?? status.LoadBalancer?.Ingress.FirstOrDefault()?.Ip
                ?? string.Empty
        );
        _eksFargateProfileArn = fargateProfile.Arn;
        _arn = _eksFargateProfileArn;
        var primaryExternalPort = PortForwardings[0].ExternalPort ?? 80;
        _serviceUrl = endpoint.Apply(value => CreateEksServiceUrl(value, primaryExternalPort));
        _dnsTarget = endpoint;

        _dependencies.AddRange(
            [
                podExecutionRole,
                podExecutionAttachment,
                fargateProfile,
                kubernetesProvider,
                deployment,
                service,
            ]
        );
        if (kubernetesNamespace is not null)
            _dependencies.Add(kubernetesNamespace);
        if (kubernetesSecret is not null)
            _dependencies.Add(kubernetesSecret);
        if (autoscaler is not null)
            _dependencies.Add(autoscaler);
    }

    private Input<string> CreateEksKubeconfig(
        string clusterName,
        Output<Pulumi.Aws.Eks.GetClusterResult> cluster,
        InvokeOutputOptions invokeOptions
    )
    {
        var region = Pulumi.Aws.GetRegion.Invoke(
            new Pulumi.Aws.GetRegionInvokeArgs(),
            invokeOptions
        );

        return Output.Tuple(cluster, region).Apply(values =>
        {
            var clusterResult = values.Item1;
            if (clusterResult.CertificateAuthorities.Length == 0)
                throw new InvalidOperationException(
                    $"EKS cluster '{clusterName}' did not return certificate authority data."
                );

            var document = new Dictionary<string, object?>
            {
                ["apiVersion"] = "v1",
                ["kind"] = "Config",
                ["current-context"] = clusterName,
                ["clusters"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = clusterName,
                        ["cluster"] = new Dictionary<string, object?>
                        {
                            ["server"] = clusterResult.Endpoint,
                            ["certificate-authority-data"] = clusterResult
                                .CertificateAuthorities[0]
                                .Data,
                        },
                    },
                },
                ["contexts"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = clusterName,
                        ["context"] = new Dictionary<string, object?>
                        {
                            ["cluster"] = clusterName,
                            ["user"] = clusterName,
                        },
                    },
                },
                ["users"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = clusterName,
                        ["user"] = new Dictionary<string, object?>
                        {
                            ["exec"] = new Dictionary<string, object?>
                            {
                                ["apiVersion"] = "client.authentication.k8s.io/v1beta1",
                                ["command"] = "aws",
                                ["args"] = new[]
                                {
                                    "eks",
                                    "get-token",
                                    "--cluster-name",
                                    clusterName,
                                    "--region",
                                    values.Item2.Region,
                                },
                            },
                        },
                    },
                },
            };
            return JsonSerializer.Serialize(document);
        });
    }

    private Dictionary<string, string> CreateEksAwaitAnnotations() =>
        _options.WaitForSteadyState
            ? []
            : new Dictionary<string, string> { ["pulumi.com/skipAwait"] = "true" };

    private Pulumi.Kubernetes.Types.Inputs.Core.V1.ContainerArgs CreateEksContainer(
        string workloadName,
        string secretName
    )
    {
        var environment = new List<Pulumi.Kubernetes.Types.Inputs.Core.V1.EnvVarArgs>();
        foreach (var (name, value) in _environmentVariables)
            environment.Add(new() { Name = name, Value = value });
        foreach (var (name, value) in _environmentVariablesOutput)
            environment.Add(new() { Name = name, Value = value });
        foreach (var item in _requiredSecrets)
        {
            var environmentVariableName = GetSecretEnvironmentVariableName(item);
            environment.Add(
                new Pulumi.Kubernetes.Types.Inputs.Core.V1.EnvVarArgs
                {
                    Name = environmentVariableName,
                    ValueFrom = new Pulumi.Kubernetes.Types.Inputs.Core.V1.EnvVarSourceArgs
                    {
                        SecretKeyRef =
                            new Pulumi.Kubernetes.Types.Inputs.Core.V1.SecretKeySelectorArgs
                            {
                                Name = secretName,
                                Key = environmentVariableName,
                            },
                    },
                }
            );
        }

        var ports = PortForwardings
            .Select(
                (port, index) =>
                    new Pulumi.Kubernetes.Types.Inputs.Core.V1.ContainerPortArgs
                    {
                        Name = index == 0 ? "http" : $"port-{index + 1}",
                        ContainerPortValue = port.ContainerPort,
                        Protocol = NormalizeKubernetesProtocol(port.Protocol),
                    }
            )
            .ToList();

        var container = new Pulumi.Kubernetes.Types.Inputs.Core.V1.ContainerArgs
        {
            Name = workloadName,
            Image = GetContainerImageUri(),
            Ports = ports,
            Env = environment,
        };

        var resources = new InputMap<string>
        {
            { "cpu", CpuLimit ?? "250m" },
            { "memory", MemoryLimit ?? "512Mi" },
        };
        container.Resources = new Pulumi.Kubernetes.Types.Inputs.Core.V1.ResourceRequirementsArgs
        {
            Limits = resources,
            Requests = resources,
        };

        if (HealthCheckConfiguration is not null)
        {
            container.ReadinessProbe = CreateEksProbe(
                HealthCheckConfiguration,
                startup: false,
                readiness: true
            );
            container.LivenessProbe = CreateEksProbe(
                HealthCheckConfiguration,
                startup: false,
                readiness: false
            );
            if (
                HealthCheckConfiguration.StartupIntervalSeconds is not null
                || HealthCheckConfiguration.StartupTimeoutSeconds is not null
                || HealthCheckConfiguration.StartupUnhealthyThreshold is not null
            )
                container.StartupProbe = CreateEksProbe(
                    HealthCheckConfiguration,
                    startup: true,
                    readiness: false
                );
        }

        return container;
    }

    private Pulumi.Kubernetes.Types.Inputs.Core.V1.ProbeArgs CreateEksProbe(
        IHealthCheckConfiguration healthCheck,
        bool startup,
        bool readiness
    ) =>
        new()
        {
            HttpGet = new Pulumi.Kubernetes.Types.Inputs.Core.V1.HTTPGetActionArgs
            {
                Path = NormalizeHealthPath(healthCheck.Path),
                Port = GetPrimaryPort(),
                Scheme = "HTTP",
            },
            PeriodSeconds = startup
                ? healthCheck.StartupIntervalSeconds ?? healthCheck.IntervalSeconds
                : healthCheck.IntervalSeconds,
            TimeoutSeconds = startup
                ? healthCheck.StartupTimeoutSeconds ?? healthCheck.TimeoutSeconds
                : healthCheck.TimeoutSeconds,
            FailureThreshold = startup
                ? healthCheck.StartupUnhealthyThreshold ?? healthCheck.UnhealthyThreshold
                : healthCheck.UnhealthyThreshold,
            // Kubernetes requires successThreshold to be exactly one for liveness and startup
            // probes. Only readiness probes may require multiple consecutive successes.
            SuccessThreshold = readiness ? healthCheck.HealthyThreshold : 1,
        };

    private List<Pulumi.Kubernetes.Types.Inputs.Core.V1.ServicePortArgs> CreateEksServicePorts() =>
        PortForwardings
            .Select(
                (port, index) =>
                    new Pulumi.Kubernetes.Types.Inputs.Core.V1.ServicePortArgs
                    {
                        Name = index == 0 ? "http" : $"port-{index + 1}",
                        Port = port.ExternalPort ?? (index == 0 ? 80 : port.ContainerPort),
                        TargetPort = port.ContainerPort,
                        Protocol = NormalizeKubernetesProtocol(port.Protocol),
                    }
            )
            .ToList();

    private static string CreateEksServiceUrl(string endpoint, int externalPort)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;

        var host = endpoint.TrimEnd('/');
        return externalPort == 80 ? $"http://{host}" : $"http://{host}:{externalPort}";
    }

    private static string NormalizeKubernetesProtocol(string? protocol)
    {
        var normalized = string.IsNullOrWhiteSpace(protocol)
            ? "TCP"
            : protocol.ToUpperInvariant();
        if (normalized is not ("TCP" or "UDP" or "SCTP"))
            throw new InvalidOperationException(
                $"Kubernetes does not support the container protocol '{protocol}'."
            );
        return normalized;
    }

    private static string GetKubernetesName(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9-]", "-")
            .Trim('-');
        if (normalized.Length > 63)
            normalized = normalized[..63].TrimEnd('-');
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException(
                $"'{value}' cannot be converted to a valid Kubernetes name.",
                nameof(value)
            );
        return normalized;
    }

    partial void AddEksPolicy(
        List<IPolicyStatement> statements,
        ComputePermissions permission
    )
    {
        switch (permission)
        {
            case ComputePermissions.GetStatus:
                AddEksPolicyStatement(statements, _eksClusterArn, "eks:DescribeCluster");
                AddEksPolicyStatement(
                    statements,
                    _eksFargateProfileArn,
                    "eks:DescribeFargateProfile"
                );
                break;
            case ComputePermissions.Start:
            case ComputePermissions.Stop:
            case ComputePermissions.Restart:
            case ComputePermissions.Scale:
            case ComputePermissions.Connect:
            case ComputePermissions.UpdateConfiguration:
            case ComputePermissions.GetLogs:
                // These are Kubernetes workload operations. AWS IAM cannot grant them; the
                // caller needs Kubernetes access and the appropriate Kubernetes RBAC rules.
                // eks:AccessKubernetesApi is an AWS-console viewing permission, not ordinary
                // Kubernetes API authorization, so intentionally do not emit it here.
                break;
            case ComputePermissions.Terminate:
                AddEksPolicyStatement(
                    statements,
                    _eksFargateProfileArn,
                    "eks:DeleteFargateProfile"
                );
                break;
        }
    }

    private static void AddEksPolicyStatement(
        List<IPolicyStatement> statements,
        Output<string> resourceArn,
        params string[] actions
    ) =>
        statements.Add(
            new AwsPolicyStatement
            {
                Effect = "Allow",
                Actions = actions,
                Resources = [resourceArn],
            }
        );
}
