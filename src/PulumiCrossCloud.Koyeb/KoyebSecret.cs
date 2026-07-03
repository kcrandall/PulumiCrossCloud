using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Koyeb;

public sealed class KoyebSecret : ISecret
{
    private readonly global::Pulumi.Koyeb.Secret _secret;
    private string _value;

    public KoyebSecret(
        string name,
        string value,
        bool ignoreChangesAfterCreate = false,
        global::Pulumi.Koyeb.Provider? provider = null
    )
    {
        Name = name;
        _value = value;
        IgnoreChangesAfterCreate = ignoreChangesAfterCreate;
        var options = new CustomResourceOptions { Provider = provider };
        if (ignoreChangesAfterCreate)
        {
            options.IgnoreChanges = ["value"];
        }

        _secret = new global::Pulumi.Koyeb.Secret(
            name,
            new global::Pulumi.Koyeb.SecretArgs
            {
                Name = name,
                Type = "simple",
                Value = value
            },
            options
        );
    }

    public string Name { get; }
    public Output<string> Arn => _secret.Id;
    public Output<string?> Value => _secret.Value;
    public bool IgnoreChangesAfterCreate { get; }
    public Resource Resource => _secret;
    public CloudProvider Provider => CloudProvider.Koyeb;

    public Task<string> GetValueAsync() => Task.FromResult(_value);

    public void SetValue(string value)
    {
        _value = value;
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params SecretPermissions[] permissions) => [];
}
