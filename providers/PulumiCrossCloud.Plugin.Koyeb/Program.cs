using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.Koyeb;

await Pulumi.Experimental.Provider.Provider.Serve(
    args,
    PluginVersion.Current,
    _ => new KoyebResourceProvider(),
    CancellationToken.None
);
