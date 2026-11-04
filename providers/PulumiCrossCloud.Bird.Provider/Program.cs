using PulumiCrossCloud.Bird.Provider;

await Pulumi.Experimental.Provider.Provider.Serve(
    args,
    BirdProviderVersion.Current,
    _ => new BirdResourceProvider(),
    CancellationToken.None
);
