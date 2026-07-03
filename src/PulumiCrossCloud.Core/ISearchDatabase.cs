using Pulumi;

namespace PulumiCrossCloud.Core;

public interface ISearchDatabase : IDatabase, IPolicyProvider<SearchDatabasePermissions>
{
    SearchDatabaseType Type { get; }
}
