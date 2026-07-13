using Pulumi;

namespace PulumiCrossCloud.Core;

public enum MemoryCachePermissions
{
    Read,
    Write,
    Delete,
    Flush,
    Admin,
}

public interface IMemoryCacheDatabase : IDatabase
{
    IEnumerable<IPolicyStatement> GetPolicy(params MemoryCachePermissions[] permissions);
}
