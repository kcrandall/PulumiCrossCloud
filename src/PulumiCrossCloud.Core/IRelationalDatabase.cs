using Pulumi;

namespace PulumiCrossCloud.Core;

public interface IRelationalDatabase : IDatabase
{
    Output<string> DatabaseName { get; }
}
