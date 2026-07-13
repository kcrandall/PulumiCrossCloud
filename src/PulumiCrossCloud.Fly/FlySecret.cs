using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Fly;

public sealed class FlySecret : ISecret
{
    private readonly global::Pulumi.Fly.Secret _secret;
    private string _value;

    public FlySecret(
        string name,
        string appName,
        string value,
        bool ignoreChangesAfterCreate = false,
        global::Pulumi.Fly.Provider? provider = null
    )
    {
        Name = name;
        _value = value;
        IgnoreChangesAfterCreate = ignoreChangesAfterCreate;
        var options = new CustomResourceOptions { Provider = provider };
        if (ignoreChangesAfterCreate)
            options.IgnoreChanges = ["value"];

        _secret = new global::Pulumi.Fly.Secret(
            name,
            new global::Pulumi.Fly.SecretArgs
            {
                App = appName,
                Key = name,
                Value = value,
            },
            options
        );
    }

    public string Name { get; }
    public Output<string> Arn => _secret.Id;
    public Output<string?> Value => _secret.Value.Apply(value => (string?)value);
    public bool IgnoreChangesAfterCreate { get; }
    public Resource Resource => _secret;
    public CloudProvider Provider => CloudProvider.Fly;

    public Task<string> GetValueAsync() => Task.FromResult(_value);

    public void SetValue(string value)
    {
        _value = value;
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params SecretPermissions[] permissions) => [];
}
