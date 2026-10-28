using Pulumi;
using Pulumi.Testing;

namespace PulumiCrossCloud.Gcp.Tests;

public sealed class GcpComputeLifecycleTests
{
    [Fact]
    public async Task BuildIsIdempotentAndReadyCallbacksRunOnceAfterServiceRegistration()
    {
        var mocks = new RecordingMocks();
        int firstCalls = 0;
        int secondCalls = 0;
        int lateCalls = 0;

        await Deployment.TestAsync(
            mocks,
            new TestOptions { IsPreview = false },
            () =>
            {
                GcpProjectConfig.ProjectId = CloudRunDeployment.ProjectId;
                GcpProjectConfig.Region = CloudRunDeployment.Region;
                var registry = new GcpContainerRegistry("lifecycle-registry");
                var compute = new GcpCloudRunCompute("lifecycle-api", registry, "api");
                compute.OnReady(_ =>
                {
                    Assert.Single(mocks.Created(CloudRunDeployment.CloudRunServiceType));
                    Interlocked.Increment(ref firstCalls);
                    return Task.CompletedTask;
                });
                compute.OnReady(_ =>
                {
                    Interlocked.Increment(ref secondCalls);
                    return Task.CompletedTask;
                });
                compute.Build();
                compute.Build();
                compute.OnReady(_ =>
                {
                    Interlocked.Increment(ref lateCalls);
                    return Task.CompletedTask;
                });
            }
        );

        Assert.Single(mocks.Created(CloudRunDeployment.CloudRunServiceType));
        Assert.Equal(1, firstCalls);
        Assert.Equal(1, secondCalls);
        Assert.Equal(1, lateCalls);
    }
}
