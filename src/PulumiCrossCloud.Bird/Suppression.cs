using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Bird;

/// <summary>
/// A manual Bird suppression (<c>crosscloud-bird:index:Suppression</c>) that stops all email to
/// one address, transactional included. Deleting it removes only this manual record: bounce or
/// complaint suppressions for the address stay in effect.
/// </summary>
public class Suppression : CustomResource
{
    public const string TypeToken = "crosscloud-bird:index:Suppression";

    [Output("email")]
    public Output<string> Email { get; private set; } = null!;

    /// <summary>
    /// Always <c>manual</c> for suppressions created here.
    /// </summary>
    [Output("reason")]
    public Output<string> Reason { get; private set; } = null!;

    public Suppression(string name, SuppressionArgs args, CustomResourceOptions? options = null)
        : base(TypeToken, name, args, CrossCloudPlugins.WithDefaults(options)) { }
}

public sealed class SuppressionArgs : ResourceArgs
{
    [Input("email", required: true)]
    public Input<string> Email { get; set; } = null!;
}
