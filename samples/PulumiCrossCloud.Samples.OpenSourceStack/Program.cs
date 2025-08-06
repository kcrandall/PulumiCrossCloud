using System.Threading.Tasks;
using Pulumi;
using PulumiCrossCloud.Samples.OpenSourceStack;

namespace PulumiCrossCloud.Samples.OpenSourceStack;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await Deployment.RunAsync<OpenSourceStack>();
    }
}
