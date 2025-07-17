using System.Text.Json;
using PulumiCrossCloud.Core;

namespace PulumiCrossCloud.Extensions;

public static class PolicyExtensions
{
    /// <summary>
    /// Combines multiple policy statements into a single collection
    /// </summary>
    public static IEnumerable<IPolicyStatement> Combine(params IEnumerable<IPolicyStatement>[] policyGroups)
    {
        return policyGroups.SelectMany(group => group);
    }
    
    /// <summary>
    /// Filters policy statements by effect (Allow/Deny)
    /// </summary>
    public static IEnumerable<IPolicyStatement> WithEffect(this IEnumerable<IPolicyStatement> statements, string effect)
    {
        return statements.Where(s => s.Effect.Equals(effect, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Filters policy statements by action patterns
    /// </summary>
    public static IEnumerable<IPolicyStatement> WithActions(this IEnumerable<IPolicyStatement> statements, params string[] actionPatterns)
    {
        return statements.Where(s => s.Actions.Any(action => 
            actionPatterns.Any(pattern => action.Contains(pattern, StringComparison.OrdinalIgnoreCase))));
    }
    
    /// <summary>
    /// Groups policy statements by their effect
    /// </summary>
    public static Dictionary<string, IEnumerable<IPolicyStatement>> GroupByEffect(this IEnumerable<IPolicyStatement> statements)
    {
        return statements.GroupBy(s => s.Effect).ToDictionary(g => g.Key, g => g.AsEnumerable());
    }
    
    /// <summary>
    /// Validates that all policy statements have required fields
    /// </summary>
    public static bool IsValid(this IPolicyStatement statement)
    {
        return !string.IsNullOrEmpty(statement.Effect) &&
               statement.Actions.Any() &&
               statement.Resources.Any();
    }
    
    /// <summary>
    /// Gets all invalid policy statements from a collection
    /// </summary>
    public static IEnumerable<IPolicyStatement> GetInvalid(this IEnumerable<IPolicyStatement> statements)
    {
        return statements.Where(s => !s.IsValid());
    }
    
    /// <summary>
    /// Creates a policy summary with counts and breakdown
    /// </summary>
    public static string GetSummary(this IEnumerable<IPolicyStatement> statements)
    {
        var statementList = statements.ToList();
        var summary = $"Policy Summary:\n";
        summary += $"Total Statements: {statementList.Count}\n";
        
        var grouped = statementList.GroupByEffect();
        foreach (var group in grouped)
        {
            summary += $"{group.Key} Statements: {group.Value.Count()}\n";
        }
        
        var invalid = statementList.GetInvalid().Count();
        if (invalid > 0)
        {
            summary += $"Invalid Statements: {invalid}\n";
        }
        
        return summary;
    }
}