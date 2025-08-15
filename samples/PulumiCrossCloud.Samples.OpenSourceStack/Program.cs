using System.Threading.Tasks;
using Pulumi;
using PulumiCrossCloud.Samples.OpenSourceStack;


return await Deployment.RunAsync(() => {
    // Create your stack components
    var openSourceStack = new PulumiCrossCloud.Samples.OpenSourceStack.OpenSourceStack();
    
    // Return any outputs you want to expose (optional)
    return new Dictionary<string, object?>();
});