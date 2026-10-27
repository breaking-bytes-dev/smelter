// Asserts E2E de SiteMapSpec.cs: ningún SubArea sin Title explícito ni con prefijos técnicos (TBL | / SMLT |) y el
// nombre de la app sin typos. Solo lee.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class SiteMapAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[SITEMAP] Verificando etiquetas de la MDA (SiteMapSpec.cs) ...");

        var sitemap = client.RetrieveMultiple(new QueryExpression("sitemap")
        {
            ColumnSet = new ColumnSet("sitemapxml"),
            Criteria = { Conditions = { new ConditionExpression("sitemapnameunique", ConditionOperator.Equal, SecuritySpec.AppModuleUniqueName) } },
        }).Entities.SingleOrDefault();
        Assert(sitemap is not null, $"sin sitemap de '{SecuritySpec.AppModuleUniqueName}'");

        var problems = SiteMapCatalogo.Violations(sitemap!.GetAttributeValue<string>("sitemapxml") ?? "");
        Assert(problems.Count == 0, "sitemap con etiquetas técnicas: " + string.Join("; ", problems));

        var app = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("name"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, MdaSpec.AppUniqueName) } },
        }).Entities.SingleOrDefault();
        Assert(app is not null, $"sin app '{MdaSpec.AppUniqueName}'");

        var name = app!.GetAttributeValue<string>("name");
        var nameProblem = SiteMapCatalogo.AppNameViolation(name);
        Assert(nameProblem is null, $"nombre de la app '{name}': {nameProblem}");
        Assert(name == SiteMapCatalogo.AppName, $"nombre de la app '{name}', esperado '{SiteMapCatalogo.AppName}'");

        Console.WriteLine($"[SITEMAP] OK: app '{name}' y sitemap sin etiquetas técnicas.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[SITEMAP]: " + message);
    }
}
