using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.Cloudflare;

await Pulumi.Experimental.Provider.Provider.Serve(
    args,
    PluginVersion.Current,
    _ => new CloudflareResourceProvider(),
    CancellationToken.None
);
