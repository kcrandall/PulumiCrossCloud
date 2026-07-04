using Pulumi;
using GeneratedSentry = Pulumi.Sentry;

namespace PulumiCrossCloud.Sentry;

public sealed class SentryCloudResources
{
    public GeneratedSentry.Provider Provider { get; }

    public SentryCloudResources(
        string name,
        Input<string> authToken,
        Input<string>? baseUrl = null
    )
    {
        Provider = new GeneratedSentry.Provider(
            $"{name}-provider",
            new GeneratedSentry.ProviderArgs
            {
                Token = authToken,
                BaseUrl = baseUrl
            }
        );
    }

    public SentryCloudResources(GeneratedSentry.Provider provider)
    {
        Provider = provider;
    }

    public GeneratedSentry.Organization CreateOrganization(
        string name,
        GeneratedSentry.OrganizationArgs args
    ) =>
        new(
            name,
            args,
            new CustomResourceOptions { Provider = Provider }
        );

    public GeneratedSentry.Team CreateTeam(string name, GeneratedSentry.TeamArgs args) =>
        new(
            name,
            args,
            new CustomResourceOptions { Provider = Provider }
        );

    public GeneratedSentry.Project CreateProject(string name, GeneratedSentry.ProjectArgs args) =>
        new(
            name,
            args,
            new CustomResourceOptions { Provider = Provider }
        );
}
