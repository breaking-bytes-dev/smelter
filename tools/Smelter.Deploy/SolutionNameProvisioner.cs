// Provisión idempotente del friendly name de la solución (SolutionNameSpec.cs): solution.friendlyname por SDK, solo si
// difiere. No toca el unique name ni la versión.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class SolutionNameProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine($"[4l] SolutionNameProvisioner: friendly name de '{SolutionNameCatalogo.UniqueName}' (SolutionNameSpec.cs) ...");

        var solution = client.RetrieveMultiple(new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid", "friendlyname"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, SolutionNameCatalogo.UniqueName) } },
        }).Entities.SingleOrDefault();

        if (solution is null)
        {
            Console.Error.WriteLine($"[4l]   WARN: solución '{SolutionNameCatalogo.UniqueName}' no encontrada — se omite el nombre.");
            return;
        }

        var current = solution.GetAttributeValue<string>("friendlyname");
        if (!SolutionNameCatalogo.NeedsUpdate(current))
        {
            Console.WriteLine($"[4l] SolutionName OK: ya es '{SolutionNameCatalogo.FriendlyName}' — nada que hacer.");
            return;
        }

        client.Update(new Entity("solution", solution.Id) { ["friendlyname"] = SolutionNameCatalogo.FriendlyName });
        Console.WriteLine($"[4l] SolutionName OK: '{current}' -> '{SolutionNameCatalogo.FriendlyName}'.");
    }
}
