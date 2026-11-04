using System.Text.Json;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.PostHog;

public sealed class PostHogOrganizationArgs : ResourceArgs
{
    /// <summary>
    /// PostHog personal API key with organization read and write scopes.
    /// </summary>
    public required Input<string> ApiKey { get; init; }
    public required Input<string> Name { get; init; }
    public Input<string> Host { get; init; } = "https://us.posthog.com";

    /// <summary>
    /// Delete the organization when this resource is destroyed. Defaults to false, so destroying a
    /// stack leaves the organization (and any it adopted by name) in place.
    /// </summary>
    public bool DeleteOnDestroy { get; init; }
}

/// <summary>
/// A PostHog organization, managed by the native <c>crosscloud-posthog</c> provider because the
/// Terraform-bridged <c>posthog</c> provider does not manage organizations. Creation adopts an
/// existing organization with the same name instead of creating a duplicate; renames update it
/// in place; refresh detects drift.
/// </summary>
public sealed class PostHogOrganization : ComponentResource
{
    public Output<string> OrganizationId { get; }
    public Output<string> Slug { get; }

    /// <summary>
    /// The organization's id, name and slug as JSON, kept for compatibility with earlier releases.
    /// </summary>
    public Output<string> RawJson { get; }

    /// <summary>
    /// The underlying <c>crosscloud-posthog:index:Organization</c> resource.
    /// </summary>
    public PostHogOrganizationResource Organization { get; }

    public PostHogOrganization(
        string name,
        PostHogOrganizationArgs args,
        ComponentResourceOptions? options = null
    )
        : base("posthog:Organization", name, null, options)
    {
        ArgumentNullException.ThrowIfNull(args);

        var provider = new CrossCloudPostHogProvider(
            $"{name}-provider",
            new CrossCloudPostHogProviderArgs { ApiKey = args.ApiKey, Host = args.Host },
            new CustomResourceOptions { Parent = this }
        );
        Organization = new PostHogOrganizationResource(
            $"{name}-organization",
            new PostHogOrganizationResourceArgs
            {
                Name = args.Name,
                DeleteOnDestroy = args.DeleteOnDestroy,
            },
            new CustomResourceOptions { Parent = this, Provider = provider }
        );

        OrganizationId = Organization.Id;
        Slug = Organization.Slug.Apply(slug => slug ?? string.Empty);
        RawJson = Output
            .Tuple(Organization.Id, Organization.Name, Slug)
            .Apply(values =>
                JsonSerializer.Serialize(
                    new Dictionary<string, string>
                    {
                        ["id"] = values.Item1,
                        ["name"] = values.Item2,
                        ["slug"] = values.Item3,
                    }
                )
            );

        RegisterOutputs(
            new Dictionary<string, object?> { ["organizationId"] = OrganizationId, ["slug"] = Slug }
        );
    }
}

/// <summary>
/// <c>crosscloud-posthog:index:Organization</c>. Prefer <see cref="PostHogOrganization"/>, which
/// also creates the provider.
/// </summary>
public class PostHogOrganizationResource : CustomResource
{
    public const string TypeToken = "crosscloud-posthog:index:Organization";

    [Output("name")]
    public Output<string> Name { get; private set; } = null!;

    [Output("slug")]
    public Output<string?> Slug { get; private set; } = null!;

    [Output("deleteOnDestroy")]
    public Output<bool> DeleteOnDestroy { get; private set; } = null!;

    /// <summary>
    /// Whether creation adopted an existing organization with the same name.
    /// </summary>
    [Output("adopted")]
    public Output<bool> Adopted { get; private set; } = null!;

    public PostHogOrganizationResource(
        string name,
        PostHogOrganizationResourceArgs args,
        CustomResourceOptions? options = null
    )
        : base(TypeToken, name, args, CrossCloudPlugins.WithDefaults(options)) { }
}

public sealed class PostHogOrganizationResourceArgs : ResourceArgs
{
    [Input("name", required: true)]
    public Input<string> Name { get; set; } = null!;

    [Input("deleteOnDestroy")]
    public Input<bool>? DeleteOnDestroy { get; set; }
}

/// <summary>
/// An explicit <c>crosscloud-posthog</c> provider. Without one, resources use the default provider
/// configured by <c>crosscloud-posthog:apiKey</c>/<c>crosscloud-posthog:host</c> stack config (or
/// <c>POSTHOG_API_KEY</c>/<c>POSTHOG_HOST</c>).
/// </summary>
public class CrossCloudPostHogProvider : ProviderResource
{
    public const string Package = "crosscloud-posthog";

    public CrossCloudPostHogProvider(
        string name,
        CrossCloudPostHogProviderArgs? args = null,
        CustomResourceOptions? options = null
    )
        : base(
            Package,
            name,
            args ?? new CrossCloudPostHogProviderArgs(),
            CrossCloudPlugins.WithDefaults(options)
        ) { }
}

public sealed class CrossCloudPostHogProviderArgs : ResourceArgs
{
    private Input<string>? _apiKey;

    /// <summary>
    /// PostHog personal API key. Stored as a secret.
    /// </summary>
    [Input("apiKey")]
    public Input<string>? ApiKey
    {
        get => _apiKey;
        set
        {
            if (value is null)
            {
                _apiKey = null;
                return;
            }

            _apiKey = Output.CreateSecret(value.Apply(key => key));
        }
    }

    [Input("host")]
    public Input<string>? Host { get; set; }
}
