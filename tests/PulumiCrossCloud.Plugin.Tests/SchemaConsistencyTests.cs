using System.Reflection;
using System.Text.Json;
using Pulumi;
using PulumiCrossCloud.Bird;
using PulumiCrossCloud.Cloudflare;
using PulumiCrossCloud.Neon;
using PulumiCrossCloud.Mailgun;
using PulumiCrossCloud.PostHog;
using BirdPlugin = PulumiCrossCloud.Plugin.Bird;
using CloudflarePlugin = PulumiCrossCloud.Plugin.Cloudflare;
using NeonPlugin = PulumiCrossCloud.Plugin.Neon;
using MailgunPlugin = PulumiCrossCloud.Plugin.Mailgun;
using PostHogPlugin = PulumiCrossCloud.Plugin.PostHog;

namespace PulumiCrossCloud.Plugin.Tests;

/// <summary>
/// The C# SDK types are written by hand against the schema each provider serves. These tests
/// catch the two drifting apart: a renamed property silently becomes a missing input or output.
/// </summary>
public sealed class SchemaConsistencyTests
{
    public static TheoryData<string, string, Type, Type> Resources =>
        new()
        {
            {
                BirdPlugin.BirdSchema.Json,
                SendingDomain.TypeToken,
                typeof(SendingDomain),
                typeof(SendingDomainArgs)
            },
            {
                BirdPlugin.BirdSchema.Json,
                DomainVerification.TypeToken,
                typeof(DomainVerification),
                typeof(DomainVerificationArgs)
            },
            {
                BirdPlugin.BirdSchema.Json,
                DomainReceiving.TypeToken,
                typeof(DomainReceiving),
                typeof(DomainReceivingArgs)
            },
            {
                PostHogPlugin.PostHogSchema.Json,
                PostHogOrganizationResource.TypeToken,
                typeof(PostHogOrganizationResource),
                typeof(PostHogOrganizationResourceArgs)
            },
            {
                MailgunPlugin.MailgunSchema.Json,
                MailgunDomainVerification.TypeToken,
                typeof(MailgunDomainVerification),
                typeof(MailgunDomainVerificationArgs)
            },
            {
                NeonPlugin.NeonSchema.Json,
                NeonEndpointSettings.TypeToken,
                typeof(NeonEndpointSettings),
                typeof(NeonEndpointSettingsArgs)
            },
            {
                CloudflarePlugin.CloudflareSchema.Json,
                CloudflareAccountToken.TypeToken,
                typeof(CloudflareAccountToken),
                typeof(CloudflareAccountTokenArgs)
            },
        };

    public static TheoryData<string, string, Type> Providers =>
        new()
        {
            { BirdPlugin.BirdSchema.Json, BirdProvider.Package, typeof(BirdProviderArgs) },
            {
                PostHogPlugin.PostHogSchema.Json,
                CrossCloudPostHogProvider.Package,
                typeof(CrossCloudPostHogProviderArgs)
            },
            {
                MailgunPlugin.MailgunSchema.Json,
                CrossCloudMailgunProvider.Package,
                typeof(CrossCloudMailgunProviderArgs)
            },
            {
                NeonPlugin.NeonSchema.Json,
                CrossCloudNeonProvider.Package,
                typeof(CrossCloudNeonProviderArgs)
            },
            {
                CloudflarePlugin.CloudflareSchema.Json,
                CrossCloudCloudflareProvider.Package,
                typeof(CrossCloudCloudflareProviderArgs)
            },
        };

    [Theory]
    [MemberData(nameof(Resources))]
    public void ResourceTypesMatchTheProviderSchema(
        string schema,
        string token,
        Type resource,
        Type args
    )
    {
        var spec = Schema(schema).GetProperty("resources").GetProperty(token);

        Assert.Equal(Keys(spec, "properties"), OutputNames(resource));
        Assert.Equal(Keys(spec, "inputProperties"), InputNames(args));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ProviderArgsMatchTheProviderSchema(string schema, string package, Type args)
    {
        var root = Schema(schema);

        Assert.Equal(package, root.GetProperty("name").GetString());
        Assert.Equal(Keys(root.GetProperty("provider"), "inputProperties"), InputNames(args));
        Assert.Equal(
            Core.CrossCloudPlugins.DownloadUrl,
            root.GetProperty("pluginDownloadURL").GetString()
        );
    }

    [Fact]
    public void BirdLookupAndRecordTypesMatchTheProviderSchema()
    {
        var root = Schema(BirdPlugin.BirdSchema.Json);
        var function = root.GetProperty("functions").GetProperty(GetSendingDomain.Token);

        Assert.Equal(
            Keys(function.GetProperty("outputs"), "properties"),
            ConstructorNames(typeof(GetSendingDomainResult))
        );
        Assert.Equal(
            Keys(function.GetProperty("inputs"), "properties"),
            InputNames(typeof(GetSendingDomainArgs))
        );
        Assert.Equal(
            Keys(function.GetProperty("inputs"), "properties"),
            InputNames(typeof(GetSendingDomainInvokeArgs))
        );
        Assert.Equal(
            Keys(
                root.GetProperty("types").GetProperty("crosscloud-bird:index:DnsRecord"),
                "properties"
            ),
            ConstructorNames(typeof(SendingDomainDnsRecord))
        );
    }

    [Fact]
    public void ProviderTypeTokensMatchTheSdk()
    {
        Assert.Equal(SendingDomain.TypeToken, BirdPlugin.SendingDomainResource.Type);
        Assert.Equal(DomainVerification.TypeToken, BirdPlugin.DomainVerificationResource.Type);
        Assert.Equal(DomainReceiving.TypeToken, BirdPlugin.DomainReceivingResource.Type);
        Assert.Equal(GetSendingDomain.Token, BirdPlugin.BirdResourceProvider.GetSendingDomain);
        Assert.Equal(BirdProvider.Package, BirdPlugin.BirdResourceProvider.Package);
        Assert.Equal(
            PostHogOrganizationResource.TypeToken,
            PostHogPlugin.OrganizationResource.Type
        );
        Assert.Equal(
            CrossCloudPostHogProvider.Package,
            PostHogPlugin.PostHogResourceProvider.Package
        );
        Assert.Equal(
            MailgunDomainVerification.TypeToken,
            MailgunPlugin.DomainVerificationResource.Type
        );
        Assert.Equal(
            CrossCloudMailgunProvider.Package,
            MailgunPlugin.MailgunResourceProvider.Package
        );
    }

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement;

    private static string[] Keys(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateObject().Select(item => item.Name).Order()];

    // Only properties the resource declares: CustomResource's own id and urn are not schema properties.
    private static string[] OutputNames(Type type) =>
        [
            .. type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
                .Select(property => property.GetCustomAttribute<OutputAttribute>()?.Name)
                .OfType<string>()
                .Order(),
        ];

    // InputAttribute keeps its name internal, so read it from the attribute's constructor argument.
    private static string[] InputNames(Type type) =>
        [
            .. type.GetProperties()
                .SelectMany(property => property.GetCustomAttributesData())
                .Where(attribute => attribute.AttributeType == typeof(InputAttribute))
                .Select(attribute => (string)attribute.ConstructorArguments[0].Value!)
                .Order(),
        ];

    private static string[] ConstructorNames(Type type) =>
        [
            .. type.GetConstructors()
                .Single(constructor =>
                    constructor.GetCustomAttribute<OutputConstructorAttribute>() is not null
                )
                .GetParameters()
                .Select(parameter => parameter.Name!)
                .Order(),
        ];
}
