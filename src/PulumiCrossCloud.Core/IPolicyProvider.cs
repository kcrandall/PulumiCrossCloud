namespace PulumiCrossCloud.Core;

public interface IPolicyProvider<TPermission> where TPermission : Enum
{
    /// <summary>
    /// Gets the policy statements needed for the specified permissions
    /// </summary>
    IEnumerable<IPolicyStatement> GetPolicy(params TPermission[] permissions);
}