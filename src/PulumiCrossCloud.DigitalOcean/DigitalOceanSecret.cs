using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.DigitalOcean;

public sealed class DigitalOceanSecret : ComponentResource, ISecret
{
    private string? _value;
    private Output<string> _secretValue;

    public DigitalOceanSecret(string name, string value, bool ignoreChangesAfterCreate = false)
        : base("crosscloud:digitalocean:Secret", name)
    {
        Name = name;
        _value = value;
        _secretValue = Output.CreateSecret(value);
        IgnoreChangesAfterCreate = ignoreChangesAfterCreate;

        RegisterOutputs(
            new Dictionary<string, object?> { ["name"] = Name, ["value"] = _secretValue }
        );
    }

    public DigitalOceanSecret(
        string name,
        Output<string> value,
        bool ignoreChangesAfterCreate = false
    )
        : base("crosscloud:digitalocean:Secret", name)
    {
        Name = name;
        _secretValue = Output.CreateSecret(value);
        IgnoreChangesAfterCreate = ignoreChangesAfterCreate;

        RegisterOutputs(
            new Dictionary<string, object?> { ["name"] = Name, ["value"] = _secretValue }
        );
    }

    public string Name { get; }
    public Output<string> Arn => Urn;
    public Output<string?> Value => _secretValue.Apply(value => (string?)value);
    public bool IgnoreChangesAfterCreate { get; }
    public Resource Resource => this;
    public CloudProvider Provider => CloudProvider.DigitalOcean;

    public Task<string> GetValueAsync() =>
        _value is not null
            ? Task.FromResult(_value)
            : throw new NotImplementedException(
                "Secret values from Output<string> are resolved during Pulumi deployment. Use the Value Output<string> property instead."
            );

    public void SetValue(string value)
    {
        _value = value;
        _secretValue = Output.CreateSecret(value);
    }

    public IEnumerable<IPolicyStatement> GetPolicy(params SecretPermissions[] permissions) => [];
}
