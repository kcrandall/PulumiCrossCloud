// using Pulumi;
// using PulumiCrossCloud.Core;
//
// namespace PulumiCrossCloud.Azure;
//
// public class AzureContainerRegistry : IContainerRegistry
// {
//     public string Name { get; private set; }
//     public Output<string> Arn { get; private set; }  // Changed to Output<string>
//     public CloudProvider Provider => CloudProvider.Azure;
//     public string RegistryUrl => throw new NotImplementedException("Use RegistryUrlOutput property for Pulumi deployments");
//     public Output<string> RegistryUrlOutput { get; private set; }
//     
//     public AzureContainerRegistry(string name)
//     {
//         Name = name;
//         // Azure ACR implementation would go here
//         Arn = Output.Create($"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.ContainerRegistry/registries/{name}");
//         RegistryUrlOutput = Output.Create($"{name}.azurecr.io");
//     }
//     
//     public void PushImage(string imageName, string tag) => throw new NotImplementedException("Azure ACR implementation needed");
//     public void PullImage(string imageName, string tag) => throw new NotImplementedException("Azure ACR implementation needed");
//     public IEnumerable<string> ListRepositories() => throw new NotImplementedException("Azure ACR implementation needed");
//     public void DeleteRepository(string repositoryName) => throw new NotImplementedException("Azure ACR implementation needed");
//     
//     public IEnumerable<IPolicyStatement> GetPolicy(params ContainerRegistryPermissions[] permissions)
//     {
//         var statements = new List<IPolicyStatement>();
//         
//         foreach (var permission in permissions)
//         {
//             switch (permission)
//             {
//                 case ContainerRegistryPermissions.Pull:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.ContainerRegistry/registries/pull/read" },
//                         Resources = new Output<string>[] { Arn }  // Fixed to use Output<string>[]
//                     });
//                     break;
//                 case ContainerRegistryPermissions.Push:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.ContainerRegistry/registries/push/write" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ContainerRegistryPermissions.List:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.ContainerRegistry/registries/read" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ContainerRegistryPermissions.Delete:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.ContainerRegistry/registries/delete" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ContainerRegistryPermissions.GetMetadata:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.ContainerRegistry/registries/read" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ContainerRegistryPermissions.ManageRepository:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { 
//                             "Microsoft.ContainerRegistry/registries/write",
//                             "Microsoft.ContainerRegistry/registries/delete"
//                         },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case ContainerRegistryPermissions.GetAuthToken:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.ContainerRegistry/registries/read" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//             }
//         }
//         
//         return statements;
//     }
// }