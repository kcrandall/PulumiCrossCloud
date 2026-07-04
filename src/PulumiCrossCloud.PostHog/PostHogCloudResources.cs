using Pulumi;
using GeneratedPostHog = Pulumi.Posthog;

namespace PulumiCrossCloud.PostHog;

public sealed class PostHogCloudResources
{
    public GeneratedPostHog.Provider Provider { get; }

    public PostHogCloudResources(
        string name,
        Input<string> apiKey,
        Input<string>? organizationId = null,
        Input<string>? projectId = null,
        Input<string>? host = null
    )
    {
        Provider = new GeneratedPostHog.Provider(
            $"{name}-provider",
            new GeneratedPostHog.ProviderArgs
            {
                ApiKey = apiKey,
                Host = host,
                OrganizationId = organizationId,
                ProjectId = projectId
            }
        );
    }

    public PostHogCloudResources(GeneratedPostHog.Provider provider)
    {
        Provider = provider;
    }

    public PostHogOrganization CreateOrganization(
        string name,
        PostHogOrganizationArgs args
    ) => new(name, args);

    public GeneratedPostHog.Project CreateProject(string name, GeneratedPostHog.ProjectArgs args) =>
        new(name, args, Options());

    public GeneratedPostHog.ProjectSettings CreateProjectSettings(
        string name,
        GeneratedPostHog.ProjectSettingsArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.FeatureFlag CreateFeatureFlag(
        string name,
        GeneratedPostHog.FeatureFlagArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.Action CreateAction(string name, GeneratedPostHog.ActionArgs args) =>
        new(name, args, Options());

    public GeneratedPostHog.Dashboard CreateDashboard(
        string name,
        GeneratedPostHog.DashboardArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.DashboardLayout CreateDashboardLayout(
        string name,
        GeneratedPostHog.DashboardLayoutArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.Insight CreateInsight(string name, GeneratedPostHog.InsightArgs args) =>
        new(name, args, Options());

    public GeneratedPostHog.Survey CreateSurvey(string name, GeneratedPostHog.SurveyArgs args) =>
        new(name, args, Options());

    public GeneratedPostHog.Alert CreateAlert(string name, GeneratedPostHog.AlertArgs args) =>
        new(name, args, Options());

    public GeneratedPostHog.HogFunction CreateHogFunction(
        string name,
        GeneratedPostHog.HogFunctionArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.ExternalDataSource CreateExternalDataSource(
        string name,
        GeneratedPostHog.ExternalDataSourceArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.ProxyRecord CreateProxyRecord(
        string name,
        GeneratedPostHog.ProxyRecordArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.OrganizationMember CreateOrganizationMember(
        string name,
        GeneratedPostHog.OrganizationMemberArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.ProjectMember CreateProjectMember(
        string name,
        GeneratedPostHog.ProjectMemberArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.Role CreateRole(string name, GeneratedPostHog.RoleArgs args) =>
        new(name, args, Options());

    public GeneratedPostHog.RoleMembership CreateRoleMembership(
        string name,
        GeneratedPostHog.RoleMembershipArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.AccessControl CreateAccessControl(
        string name,
        GeneratedPostHog.AccessControlArgs args
    ) => new(name, args, Options());

    public GeneratedPostHog.ProjectDefaultAccess CreateProjectDefaultAccess(
        string name,
        GeneratedPostHog.ProjectDefaultAccessArgs args
    ) => new(name, args, Options());

    private CustomResourceOptions Options() => new() { Provider = Provider };
}
