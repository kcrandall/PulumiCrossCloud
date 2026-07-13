using System.Text.Json;
using Pulumi;

namespace PulumiCrossCloud.Core;

public static class PolicyConverter
{
    public static Output<string> ConvertToCloudFormat(
        IEnumerable<IPolicyStatement> statements,
        CloudProvider provider
    )
    {
        return provider switch
        {
            CloudProvider.AWS => ConvertToAwsPolicy(statements),
            CloudProvider.Azure => ConvertToAzureRoleDefinition(statements),
            CloudProvider.GCP => ConvertToGcpPolicy(statements),
            _ => Output.Create($"Unsupported provider: {provider}"),
        };
    }

    private static Output<string> ConvertToAwsPolicy(IEnumerable<IPolicyStatement> statements)
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
                            Condition = statement.Conditions.Count > 0
                                ? statement.Conditions
                                : null,
                        }
                    );
                }

                var policy = new { Version = "2012-10-17", Statement = policyStatements };

                return JsonSerializer.Serialize(
                    policy,
                    new JsonSerializerOptions { WriteIndented = true }
                );
            });
    }

    private static Output<string> ConvertToAzureRoleDefinition(
        IEnumerable<IPolicyStatement> statements
    )
    {
        var statementsList = statements.ToList();
        var allResourceOutputs = statementsList.SelectMany(s => s.Resources).ToArray();

        return Output
            .All(allResourceOutputs)
            .Apply(allResources =>
            {
                var roleDefinition = new
                {
                    properties = new
                    {
                        roleName = "CustomRole",
                        description = "Generated role from policy statements",
                        permissions = statementsList.Select(s => new
                        {
                            actions = s.Actions.ToArray(),
                            notActions = new string[0],
                            dataActions = new string[0],
                            notDataActions = new string[0],
                        }),
                        assignableScopes = allResources.Distinct().ToArray(),
                    },
                };

                return JsonSerializer.Serialize(
                    roleDefinition,
                    new JsonSerializerOptions { WriteIndented = true }
                );
            });
    }

    private static Output<string> ConvertToGcpPolicy(IEnumerable<IPolicyStatement> statements)
    {
        var statementsList = statements.ToList();
        var allResourceOutputs = statementsList.SelectMany(s => s.Resources).ToArray();

        return Output
            .All(allResourceOutputs)
            .Apply(allResources =>
            {
                var policy = new
                {
                    bindings = statementsList
                        .Select(s => new
                        {
                            role = string.Join(",", s.Actions),
                            members = new string[0],
                            condition = s.Conditions.Count > 0
                                ? new
                                {
                                    expression = s.Conditions.ContainsKey("expression")
                                        ? s.Conditions["expression"]?.ToString() ?? ""
                                        : "",
                                    title = s.Conditions.ContainsKey("title")
                                        ? s.Conditions["title"]?.ToString() ?? ""
                                        : "",
                                    description = s.Conditions.ContainsKey("description")
                                        ? s.Conditions["description"]?.ToString() ?? ""
                                        : "",
                                }
                                : null,
                        })
                        .ToArray(),
                };

                return JsonSerializer.Serialize(
                    policy,
                    new JsonSerializerOptions { WriteIndented = true }
                );
            });
    }
}
