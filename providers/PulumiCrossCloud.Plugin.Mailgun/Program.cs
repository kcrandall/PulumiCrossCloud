using PulumiCrossCloud.Plugin.Common;
using PulumiCrossCloud.Plugin.Mailgun;

await Pulumi.Experimental.Provider.Provider.Serve(
    args,
    PluginVersion.Current,
    _ => new MailgunResourceProvider(),
    CancellationToken.None
);
