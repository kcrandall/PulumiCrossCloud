using Pulumi;
using PulumiverseGrafana = Pulumiverse.Grafana;

namespace PulumiCrossCloud.Grafana;

public sealed class GrafanaCloudResources
{
    public PulumiverseGrafana.Provider Provider { get; }

    public GrafanaCloudResources(string name, Input<string> auth, Input<string> url)
    {
        Provider = new PulumiverseGrafana.Provider(
            $"{name}-provider",
            new PulumiverseGrafana.ProviderArgs { Auth = auth, Url = url }
        );
    }

    public GrafanaCloudResources(PulumiverseGrafana.Provider provider)
    {
        Provider = provider;
    }

    public PulumiverseGrafana.Oss.Folder CreateFolder(
        string name,
        PulumiverseGrafana.Oss.FolderArgs args
    ) => new(name, args, new CustomResourceOptions { Provider = Provider });

    public PulumiverseGrafana.Oss.Dashboard CreateDashboard(
        string name,
        PulumiverseGrafana.Oss.DashboardArgs args
    ) => new(name, args, new CustomResourceOptions { Provider = Provider });

    public PulumiverseGrafana.Oss.DataSource CreateDataSource(
        string name,
        PulumiverseGrafana.Oss.DataSourceArgs args
    ) => new(name, args, new CustomResourceOptions { Provider = Provider });

    public PulumiverseGrafana.Oss.ServiceAccount CreateServiceAccount(
        string name,
        PulumiverseGrafana.Oss.ServiceAccountArgs args
    ) => new(name, args, new CustomResourceOptions { Provider = Provider });

    public PulumiverseGrafana.Oss.Team CreateTeam(
        string name,
        PulumiverseGrafana.Oss.TeamArgs args
    ) => new(name, args, new CustomResourceOptions { Provider = Provider });
}
