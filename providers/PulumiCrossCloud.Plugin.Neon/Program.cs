using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.Neon;

await Pulumi.Experimental.Provider.Provider.Serve(
    args,
    PluginVersion.Current,
    _ => new NeonResourceProvider(),
    CancellationToken.None
);
