using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Sentry;

public static class SentryHostingExtensions
{
    public static SentryHostedService CreateSentry(
        this CloudResourceFactory factory,
        string name,
        SentryHostingArgs args
    ) => new(factory, name, args);

    public static ICompute CreateSentryCompute(
        this CloudResourceFactory factory,
        string name,
        SentryHostingArgs args
    ) => factory.CreateSentry(name, args).Compute;

    public static SentryRelayService CreateSentryRelay(
        this CloudResourceFactory factory,
        string name,
        SentryRelayHostingArgs args
    ) => new(factory, name, args);

    public static ICompute CreateSentryRelayCompute(
        this CloudResourceFactory factory,
        string name,
        SentryRelayHostingArgs args
    ) => factory.CreateSentryRelay(name, args).Compute;
}
