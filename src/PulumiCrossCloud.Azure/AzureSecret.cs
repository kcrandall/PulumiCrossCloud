// using Pulumi;
// using PulumiCrossCloud.Core;
//
// namespace PulumiCrossCloud.Azure;
//
// public class AzureSecret : ISecret
// {
//     public string Name { get; private set; }
//     public Output<string> Value { get; private set; }
//     public Output<string> Arn { get; private set; }  // Changed to Output<string>
//     public CloudProvider Provider => CloudProvider.Azure;
//     
//     public AzureSecret(string name, string value)
//     {
//         Name = name;
//         // Azure Key Vault implementation would go here
//         // For now, this is a placeholder
//         Arn = Output.Create($"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/vault/secrets/{name}");
//         Value = Output.Create(value);
//     }
//     
//     public Task<string> GetValueAsync()
//     {
//         throw new NotImplementedException("Secret values are resolved during Pulumi deployment. Use the Value Output<string> property instead.");
//     }
//     
//     public void SetValue(string value) 
//     { 
//         throw new NotImplementedException("Use Pulumi resource updates to change secret values"); 
//     }
//     
//     public IEnumerable<IPolicyStatement> GetPolicy(params SecretPermissions[] permissions)
//     {
//         var statements = new List<IPolicyStatement>();
//         
//         foreach (var permission in permissions)
//         {
//             switch (permission)
//             {
//                 case SecretPermissions.Read:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.KeyVault/vaults/secrets/getSecret/action" },
//                         DataActions = new[] { "Microsoft.KeyVault/vaults/secrets/getSecret/action" },
//                         Resources = new Output<string>[] { Arn }  // Fixed to use Output<string>[]
//                     });
//                     break;
//                 case SecretPermissions.Write:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.KeyVault/vaults/secrets/setSecret/action" },
//                         DataActions = new[] { "Microsoft.KeyVault/vaults/secrets/setSecret/action" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case SecretPermissions.Delete:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.KeyVault/vaults/secrets/delete" },
//                         DataActions = new[] { "Microsoft.KeyVault/vaults/secrets/delete" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//                 case SecretPermissions.List:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.KeyVault/vaults/secrets/readMetadata/action" },
//                         DataActions = new[] { "Microsoft.KeyVault/vaults/secrets/readMetadata/action" },
//                         Resources = new Output<string>[] { Arn.Apply(arn => arn.Replace($"/secrets/{Name}", "/secrets/*")) }
//                     });
//                     break;
//                 case SecretPermissions.GetMetadata:
//                     statements.Add(new AzurePolicyStatement
//                     {
//                         Effect = "Allow",
//                         Actions = new[] { "Microsoft.KeyVault/vaults/secrets/readMetadata/action" },
//                         DataActions = new[] { "Microsoft.KeyVault/vaults/secrets/readMetadata/action" },
//                         Resources = new Output<string>[] { Arn }
//                     });
//                     break;
//             }
//         }
//         
//         return statements;
//     }
// }