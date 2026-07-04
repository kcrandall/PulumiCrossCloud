using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.PostHog;

public static class PostHogHostingExtensions
{
    public static PostHogHostedService CreatePostHog(
        this CloudResourceFactory factory,
        string name,
        PostHogHostingArgs args
    ) => new(factory, name, args);

    public static ICompute CreatePostHogCompute(
        this CloudResourceFactory factory,
        string name,
        PostHogHostingArgs args
    ) => factory.CreatePostHog(name, args).Compute;

    public static PostHogManagedHostedService CreateManagedPostHog(
        this CloudResourceFactory factory,
        string name,
        PostHogManagedHostingArgs args
    ) => new(factory, name, args);
}
