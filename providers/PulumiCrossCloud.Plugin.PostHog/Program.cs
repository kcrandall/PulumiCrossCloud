using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.PostHog;

await Pulumi.Experimental.Provider.Provider.Serve(
    args,
    PluginVersion.Current,
    _ => new PostHogResourceProvider(),
    CancellationToken.None
);
