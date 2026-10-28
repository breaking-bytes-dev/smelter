// Asserts E2E de SolutionNameSpec.cs: el friendly name de la solución smlt_core_smelter es el de la spec. Solo lee.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class SolutionNameAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine($"[SOLNAME] Verificando el friendly name de '{SolutionNameCatalogo.UniqueName}' (SolutionNameSpec.cs) ...");

        var solution = client.RetrieveMultiple(new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("friendlyname"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, SolutionNameCatalogo.UniqueName) } },
        }).Entities.SingleOrDefault();
        Assert(solution is not null, $"no existe la solución '{SolutionNameCatalogo.UniqueName}'");

        var name = solution!.GetAttributeValue<string>("friendlyname");
        Assert(!SolutionNameCatalogo.NeedsUpdate(name), $"friendly name '{name}', esperado '{SolutionNameCatalogo.FriendlyName}'");

        Console.WriteLine($"[SOLNAME] OK: '{name}'.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[SOLNAME]: " + message);
    }
}
