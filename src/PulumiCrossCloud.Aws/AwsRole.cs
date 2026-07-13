using System.Text.Json;
using Pulumi;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Aws;

public class AwsRole : IRole
{
    private readonly Pulumi.Aws.Iam.Role _role;

    public AwsRole(string name, string assumeRolePolicy, Pulumi.Aws.Provider? provider = null)
    {
        Name = name;
        var customResourceOptions = new CustomResourceOptions { Provider = provider };
        _role = new Pulumi.Aws.Iam.Role(
            name,
            new Pulumi.Aws.Iam.RoleArgs { AssumeRolePolicy = assumeRolePolicy },
            customResourceOptions
        );
    }

    public string Name { get; }
    public Output<string> Arn => _role.Arn;
    public CloudProvider Provider => CloudProvider.AWS;

    public void AttachPolicies(
        IEnumerable<IPolicyStatement> policies
    ) { /* Implementation */
    }

    public void AttachInlinePolicy(string policyName, IEnumerable<IPolicyStatement> statements)
    {
        var policyDocumentOutput = CreatePolicyDocument(statements);

        new Pulumi.Aws.Iam.RolePolicy(
            $"{Name}-{policyName}",
            new Pulumi.Aws.Iam.RolePolicyArgs
            {
                Role = _role.Name,
                Policy = policyDocumentOutput, // Now using Output<string>
            }
        );
    }

    private Output<string> CreatePolicyDocument(IEnumerable<IPolicyStatement> statements)
    {
        var statementsList = statements.ToList();

        // Collect all resource outputs from all statements
        var allResourceOutputs = statementsList.SelectMany(s => s.Resources).ToArray();

        // Use Output.All to resolve all resources at once
        return Output
            .All(allResourceOutputs)
            .Apply(allResources =>
            {
                var resourceIndex = 0;
                var policyStatements = new List<object>();

                foreach (var statement in statementsList)
                {
                    var statementResourceCount = statement.Resources.Count();
                    var statementResources = allResources
                        .Skip(resourceIndex)
                        .Take(statementResourceCount)
                        .ToArray();
                    resourceIndex += statementResourceCount;

                    policyStatements.Add(
                        new
                        {
                            Effect = statement.Effect,
                            Action = statement.Actions.ToArray(),
                            Resource = statementResources,
                        }
                    );
                }

                var policy = new { Version = "2012-10-17", Statement = policyStatements };

                return JsonSerializer.Serialize(policy);
            });
    }
}
