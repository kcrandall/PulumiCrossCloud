using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Pulumi;
using Pulumi.Testing;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PulumiCrossCloud.Gcp.Tests;

public sealed class GcpCloudRunComputeTests(CloudRunDeployment deployment)
    : IClassFixture<CloudRunDeployment>
{
    [Fact]
    public void PipelineOwnedImage_LeavesTheContainerImageToThePipeline()
    {
        string[] expected = [GcpCloudRunCompute.ContainerImagePropertyPath];

        Assert.Equal(
            expected,
            deployment.OptionsFor(CloudRunDeployment.PipelineOwnedService).IgnoreChanges
        );
    }

    [Fact]
    public void PulumiOwnedImage_IsTheDefaultAndKeepsPulumiInCharge()
    {
        Assert.Empty(deployment.OptionsFor(CloudRunDeployment.PulumiOwnedService).IgnoreChanges);
    }

    [Theory]
    [InlineData("always-allocated-api", false, 1)]
    [InlineData("scale-to-zero-api", true, 0)]
    public void CpuAllocationAndMinimumInstances_ReachTheCloudRunInputs(
        string name,
        bool cpuIdle,
        int minimum
    )
    {
        var inputs = deployment.ServiceInputs(name);
        Assert.True(
            PulumiPropertyPath.TryResolve(
                inputs,
                "template.containers[0].resources.cpuIdle",
                out var idle
            )
        );
        Assert.Equal(cpuIdle, idle);
        Assert.True(
            PulumiPropertyPath.TryResolve(inputs, "scaling.minInstanceCount", out var serviceMin)
        );
        Assert.True(
            PulumiPropertyPath.TryResolve(
                inputs,
                "template.scaling.minInstanceCount",
                out var revisionMin
            )
        );
        Assert.Equal(minimum, Convert.ToInt32(serviceMin));
        Assert.Equal(minimum, Convert.ToInt32(revisionMin));
    }

    [Fact]
    public void UnspecifiedCpuAllocation_PreservesTheCloudRunDefault()
    {
        var inputs = deployment.ServiceInputs(CloudRunDeployment.PulumiOwnedService);
        var present = PulumiPropertyPath.TryResolve(
            inputs,
            "template.containers[0].resources.cpuIdle",
            out var idle
        );
        Assert.True(!present || idle is null);
    }

    /// <summary>
    /// The ignore path is matched by the engine against the inputs the SDK serializes, so a typo
    /// (C# casing, a renamed field, a container collection that is not a list) is a silent no-op that
    /// still looks like a working deployment. Resolving the path over the inputs this registration
    /// actually sends is what makes that failure loud.
    /// </summary>
    [Fact]
    public void ContainerImagePropertyPath_AddressesTheImageInTheSerializedInputs()
    {
        var inputs = deployment.ServiceInputs(CloudRunDeployment.PipelineOwnedService);

        Assert.True(
            PulumiPropertyPath.TryResolve(
                inputs,
                GcpCloudRunCompute.ContainerImagePropertyPath,
                out var image
            ),
            $"'{GcpCloudRunCompute.ContainerImagePropertyPath}' does not address anything in the "
                + "Cloud Run service inputs, so ignoring it would do nothing."
        );
        Assert.Equal(CloudRunDeployment.ExpectedImageUri, image);
    }

    /// <summary>
    /// Cloud Run cannot create a revision without an image, so the opt-in must not stop Pulumi from
    /// supplying one on the first up.
    /// </summary>
    [Fact]
    public void PipelineOwnedImage_StillShipsAnImageOnCreate()
    {
        var pipelineOwned = deployment.ServiceInputs(CloudRunDeployment.PipelineOwnedService);
        var pulumiOwned = deployment.ServiceInputs(CloudRunDeployment.PulumiOwnedService);

        PulumiPropertyPath.TryResolve(
            pipelineOwned,
            GcpCloudRunCompute.ContainerImagePropertyPath,
            out var pipelineImage
        );
        PulumiPropertyPath.TryResolve(
            pulumiOwned,
            GcpCloudRunCompute.ContainerImagePropertyPath,
            out var pulumiImage
        );

        Assert.Equal(pulumiImage, pipelineImage);
    }

    /// <summary>
    /// Only the image is handed over: everything else about the service stays Pulumi's to manage.
    /// </summary>
    [Fact]
    public void PipelineOwnedImage_DoesNotIgnoreTheRestOfTheService()
    {
        var pipelineOwned = deployment.ServiceInputs(CloudRunDeployment.PipelineOwnedService);
        var pulumiOwned = deployment.ServiceInputs(CloudRunDeployment.PulumiOwnedService);

        Assert.Equal(pulumiOwned.Keys.OrderBy(key => key), pipelineOwned.Keys.OrderBy(key => key));
        Assert.Equal("INGRESS_TRAFFIC_ALL", pipelineOwned["ingress"]);
    }
}

/// <summary>
/// Builds every Cloud Run service the tests inspect in a single Pulumi deployment.
/// </summary>
public sealed class CloudRunDeployment : IAsyncLifetime
{
    public const string PipelineOwnedService = "pipeline-owned-api";
    public const string PulumiOwnedService = "pulumi-owned-api";
    public const string ProjectId = "unit-test-project";
    public const string Region = "us-central1";
    public const string RegistryName = "test-registry";
    public const string ImageName = "api";
    public const string ImageTag = "built-by-pulumi";
    public const string CloudRunServiceType = "gcp:cloudrunv2/service:Service";

    public const string ExpectedImageUri =
        $"{Region}-docker.pkg.dev/{ProjectId}/{RegistryName}/{ImageName}:{ImageTag}";

    private readonly RecordingMocks _mocks = new();

    public async Task InitializeAsync()
    {
        GcpProjectConfig.ProjectId = ProjectId;
        GcpProjectConfig.Region = Region;
        await Deployment.TestAsync<CloudRunStack>(_mocks, new TestOptions { IsPreview = false });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The resource options Pulumi resolved for a Cloud Run service, captured by a stack-wide resource
    /// transformation because <see cref="MockResourceArgs"/> only exposes inputs.
    /// </summary>
    public ResourceOptions OptionsFor(string serviceName) =>
        CloudRunStack.RecordedOptions[$"{CloudRunServiceType}::{serviceName}"];

    /// <summary>
    /// The inputs the registration sends to the engine, which is what ignore paths are matched against.
    /// </summary>
    public ImmutableDictionary<string, object> ServiceInputs(string serviceName) =>
        _mocks.Created(CloudRunServiceType).Single(resource => resource.Name == serviceName).Inputs;
}

public sealed class CloudRunStack : Pulumi.Stack
{
    public static readonly ConcurrentDictionary<string, ResourceOptions> RecordedOptions = new();

    public CloudRunStack()
        : base(new StackOptions { ResourceTransformations = [Record] })
    {
        var registry = new GcpContainerRegistry(CloudRunDeployment.RegistryName);

        var pipelineOwned = new GcpCloudRunCompute(
            CloudRunDeployment.PipelineOwnedService,
            registry,
            CloudRunDeployment.ImageName,
            CloudRunDeployment.ImageTag
        )
        {
            ImageDeployedByPipeline = true,
        };
        pipelineOwned.Build();

        var pulumiOwned = new GcpCloudRunCompute(
            CloudRunDeployment.PulumiOwnedService,
            registry,
            CloudRunDeployment.ImageName,
            CloudRunDeployment.ImageTag
        );
        pulumiOwned.Build();

        new GcpCloudRunCompute(
            "always-allocated-api",
            registry,
            CloudRunDeployment.ImageName,
            CloudRunDeployment.ImageTag
        )
        {
            CpuIdle = false,
            MinCount = 1,
            CpuLimit = "1",
        }.Build();
        new GcpCloudRunCompute(
            "scale-to-zero-api",
            registry,
            CloudRunDeployment.ImageName,
            CloudRunDeployment.ImageTag
        )
        {
            CpuIdle = true,
            MinCount = 0,
        }.Build();
    }

    private static ResourceTransformationResult? Record(ResourceTransformationArgs args)
    {
        RecordedOptions[$"{args.Resource.GetResourceType()}::{args.Resource.GetResourceName()}"] =
            args.Options;
        return null;
    }
}

public sealed class RecordingMocks : IMocks
{
    private readonly ConcurrentQueue<MockResourceArgs> _resources = new();

    public IEnumerable<MockResourceArgs> Created(string type) =>
        _resources.Where(resource => resource.Type == type && string.IsNullOrEmpty(resource.Id));

    public Task<(string? id, object state)> NewResourceAsync(MockResourceArgs args)
    {
        _resources.Enqueue(args);

        var name = args.Name ?? "resource";
        var state = args.Inputs.ToDictionary(pair => pair.Key, pair => pair.Value);
        state.TryAdd("name", name);
        state.TryAdd("email", $"{name}@{CloudRunDeployment.ProjectId}.iam.gserviceaccount.com");
        state.TryAdd("uri", $"https://{name}.a.run.app");

        return Task.FromResult<(string? id, object state)>(($"{name}-id", state));
    }

    public Task<object> CallAsync(MockCallArgs args) =>
        Task.FromResult<object>(ImmutableDictionary<string, object>.Empty);
}

/// <summary>
/// Walks a Pulumi property path — the syntax <c>CustomResourceOptions.IgnoreChanges</c> takes — over a
/// serialized resource registration. Mirrors the engine's parser: dotted segments index objects and
/// bracketed integers index lists, both against provider input names rather than C# property names.
/// </summary>
internal static class PulumiPropertyPath
{
    public static bool TryResolve(object? root, string path, out object? value)
    {
        value = root;
        foreach (var segment in Parse(path))
        {
            var resolved = segment switch
            {
                int index => TryGetElement(value, index, out value),
                string key => TryGetMember(value, key, out value),
                _ => false,
            };

            if (!resolved)
                return false;
        }

        return true;
    }

    private static List<object> Parse(string path)
    {
        var segments = new List<object>();
        var position = 0;

        while (position < path.Length)
        {
            if (path[position] == '.')
            {
                position++;
                continue;
            }

            if (path[position] == '[')
            {
                var close = path.IndexOf(']', position);
                if (close < 0)
                    throw new FormatException($"Unterminated '[' in property path '{path}'.");

                var inner = path[(position + 1)..close];
                if (!int.TryParse(inner, out var index))
                    throw new FormatException(
                        $"Only integer indices are supported here; got '[{inner}]' in '{path}'."
                    );

                segments.Add(index);
                position = close + 1;
                continue;
            }

            var next = path.IndexOfAny(['.', '['], position);
            if (next < 0)
                next = path.Length;

            segments.Add(path[position..next]);
            position = next;
        }

        return segments;
    }

    private static bool TryGetMember(object? container, string key, out object? value)
    {
        value = null;
        switch (container)
        {
            case IDictionary<string, object> typed:
                return typed.TryGetValue(key, out value);
            case IReadOnlyDictionary<string, object> readOnly:
                return readOnly.TryGetValue(key, out value);
            case IDictionary untyped when untyped.Contains(key):
                value = untyped[key];
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetElement(object? container, int index, out object? value)
    {
        value = null;
        if (container is string || container is not IEnumerable items)
            return false;

        var elements = items.Cast<object?>().ToList();
        if (index < 0 || index >= elements.Count)
            return false;

        value = elements[index];
        return true;
    }
}
