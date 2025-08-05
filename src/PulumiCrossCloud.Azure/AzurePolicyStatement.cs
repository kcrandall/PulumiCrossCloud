// using System.Text.Json;
// using PulumiCrossCloud.Core;
//
// namespace PulumiCrossCloud.Azure;
//
// public class AzurePolicyStatement : BasePolicyStatement
// {
//     public IEnumerable<string> NotActions { get; set; } = new List<string>();
//     public IEnumerable<string> DataActions { get; set; } = new List<string>();
//     public IEnumerable<string> NotDataActions { get; set; } = new List<string>();
//     
//     public override string ToNativeFormat()
//     {
//         var roleDefinition = new
//         {
//             properties = new
//             {
//                 roleName = "CustomRole",
//                 description = "Generated role",
//                 permissions = new[]
//                 {
//                     new
//                     {
//                         actions = Actions,
//                         notActions = NotActions,
//                         dataActions = DataActions,
//                         notDataActions = NotDataActions
//                     }
//                 },
//                 assignableScopes = Resources
//             }
//         };
//         
//         return JsonSerializer.Serialize(roleDefinition, new JsonSerializerOptions { WriteIndented = true });
//     }
// }