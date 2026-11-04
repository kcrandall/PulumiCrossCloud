using PulumiCrossCloud.Plugin.Bird;
using PulumiCrossCloud.Plugin.Common;

await Pulumi.Experimental.Provider.Provider.Serve(
    args,
    PluginVersion.Current,
    _ => new BirdResourceProvider(),
    CancellationToken.None
);
