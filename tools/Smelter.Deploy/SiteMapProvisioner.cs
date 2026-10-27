// Provisión idempotente de las etiquetas de la MDA (SiteMapSpec.cs): títulos del sitemapxml (Area/Group/SubArea) y
// nombre de la app (appmodule.name). Un sitemap/nombre que ya coincide no se escribe. Publica solo la app. No
// destructivo y sin tocar display names de tablas/columnas.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class SiteMapProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4k] SiteMapProvisioner: etiquetas de la MDA (SiteMapSpec.cs) ...");
        var touched = false;

        var sitemap = client.RetrieveMultiple(new QueryExpression("sitemap")
        {
            ColumnSet = new ColumnSet("sitemapid", "sitemapxml"),
            Criteria = { Conditions = { new ConditionExpression("sitemapnameunique", ConditionOperator.Equal, SecuritySpec.AppModuleUniqueName) } },
        }).Entities.SingleOrDefault();

        if (sitemap is null)
        {
            Console.Error.WriteLine($"[4k]   WARN: sitemap de '{SecuritySpec.AppModuleUniqueName}' no encontrado — se omiten los títulos.");
        }
        else
        {
            var (xml, changed) = SiteMapCatalogo.Apply(sitemap.GetAttributeValue<string>("sitemapxml") ?? "");
            if (changed)
            {
                client.Update(new Entity("sitemap", sitemap.Id) { ["sitemapxml"] = xml });
                Console.WriteLine("[4k]   sitemap: títulos actualizados.");
                touched = true;
            }
            else
            {
                Console.WriteLine("[4k]   sitemap: títulos ya al día — nada que hacer.");
            }
        }

        var app = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("appmoduleid", "name"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, MdaSpec.AppUniqueName) } },
        }).Entities.SingleOrDefault();

        if (app is null)
        {
            Console.Error.WriteLine($"[4k]   WARN: app '{MdaSpec.AppUniqueName}' no encontrada — se omite el nombre.");
        }
        else if (app.GetAttributeValue<string>("name") != SiteMapCatalogo.AppName)
        {
            client.Update(new Entity("appmodule", app.Id) { ["name"] = SiteMapCatalogo.AppName });
            Console.WriteLine($"[4k]   app: nombre = '{SiteMapCatalogo.AppName}'.");
            touched = true;
        }
        else
        {
            Console.WriteLine("[4k]   app: nombre ya al día — nada que hacer.");
        }

        if (touched && app is not null)
        {
            try { client.Execute(new PublishXmlRequest { ParameterXml = MdaComponentsCatalogo.PublishAppXml(app.Id.ToString()) }); }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                client.Execute(new PublishXmlRequest { ParameterXml = MdaComponentsCatalogo.PublishAppXml(MdaSpec.AppUniqueName) });
            }
            Console.WriteLine("[4k]   app publicada.");
        }

        Console.WriteLine("[4k] SiteMap OK.");
    }
}
