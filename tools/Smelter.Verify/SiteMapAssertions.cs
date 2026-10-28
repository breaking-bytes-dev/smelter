// Asserts E2E de SiteMapSpec.cs: ningún Area/Group/SubArea sin Title explícito en 1033 y 3082 ni con prefijos técnicos
// (TBL | / SMLT |), cada SubArea con su VectorIcon, y el nombre de la app en inglés (1033) y español (3082) sin typos.
// Solo lee.

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
        Assert(problems.Count == 0, "sitemap con etiquetas técnicas o sin los dos idiomas: " + string.Join("; ", problems));

        var xml = sitemap.GetAttributeValue<string>("sitemapxml") ?? "";
        var (_, pending) = SiteMapCatalogo.Apply(xml);
        Assert(!pending, "el sitemap no está al día con SiteMapSpec (títulos 1033/3082 o íconos): re-corré Smelter.Deploy");
        var (_, dashPending) = DashboardCatalogo.PointSiteMapAtDashboard(xml);
        Assert(!dashPending, $"el SubArea '{DashboardCatalogo.SubAreaId}' no apunta al dashboard clásico {DashboardCatalogo.FormId}: re-corré Smelter.Deploy");
        var iconProblems = SiteMapCatalogo.IconViolations(xml);
        Assert(iconProblems.Count == 0, "sitemap con íconos incorrectos: " + string.Join("; ", iconProblems));

        var app = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("appmoduleid", "name"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, MdaSpec.AppUniqueName) } },
        }).Entities.SingleOrDefault();
        Assert(app is not null, $"sin app '{MdaSpec.AppUniqueName}'");

        var name = app!.GetAttributeValue<string>("name");
        var nameProblem = SiteMapCatalogo.AppNameViolation(name);
        Assert(nameProblem is null, $"nombre de la app '{name}': {nameProblem}");
        Assert(name == SiteMapCatalogo.AppName.En, $"nombre de la app '{name}' (1033), esperado '{SiteMapCatalogo.AppName.En}'");

        var labels = LocLabels.Get(client, "appmodule", app.Id, "name");
        Assert(LocLabels.Matches(labels, SiteMapCatalogo.AppName),
            $"nombre de la app en 1033/3082 = '{labels.GetValueOrDefault(Lcids.English)}' / '{labels.GetValueOrDefault(Lcids.Spanish)}', " +
            $"esperado '{SiteMapCatalogo.AppName.En}' / '{SiteMapCatalogo.AppName.Es}'");

        Console.WriteLine($"[SITEMAP] OK: app '{name}' (+ 3082 '{SiteMapCatalogo.AppName.Es}') y sitemap bilingüe, sin etiquetas técnicas, con íconos.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[SITEMAP]: " + message);
    }
}
