using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Grafana;

public static class GrafanaHostingExtensions
{
    public static GrafanaHostedService CreateGrafana(
        this CloudResourceFactory factory,
        string name,
        GrafanaHostingArgs args
    ) => new(factory, name, args);

    public static ICompute CreateGrafanaCompute(
        this CloudResourceFactory factory,
        string name,
        GrafanaHostingArgs args
    ) => factory.CreateGrafana(name, args).Compute;

    public static GrafanaManagedHostedService CreateManagedGrafana(
        this CloudResourceFactory factory,
        string name,
        GrafanaManagedHostingArgs args
    ) => new(factory, name, args);
}
