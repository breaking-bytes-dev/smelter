// Provisión idempotente de las etiquetas de la MDA (SiteMapSpec.cs): títulos bilingües (1033 inglés + 3082 español) e
// íconos (VectorIcon) del sitemapxml (Area/Group/SubArea) y nombre de la app (appmodule.name = inglés base; el 3082 con
// SetLocLabelsRequest). Un sitemap/nombre que ya coincide no se escribe. Publica solo la app. No destructivo y sin tocar
// display names de tablas/columnas.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class SiteMapProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4k] SiteMapProvisioner: etiquetas e íconos de la MDA (SiteMapSpec.cs) ...");
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
                Console.WriteLine("[4k]   sitemap: títulos (1033 + 3082) e íconos actualizados.");
                touched = true;
            }
            else
            {
                Console.WriteLine("[4k]   sitemap: títulos e íconos ya al día — nada que hacer.");
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
        else
        {
            var appTouched = false;
            // 1033 (valor base) con Update normal; 3082 con SetLocLabels (el Update del base no toca las traducciones).
            if (app.GetAttributeValue<string>("name") != SiteMapCatalogo.AppName.En)
            {
                client.Update(new Entity("appmodule", app.Id) { ["name"] = SiteMapCatalogo.AppName.En });
                Console.WriteLine($"[4k]   app: nombre (1033) = '{SiteMapCatalogo.AppName.En}'.");
                appTouched = true; touched = true;
            }

            try
            {
                if (LocLabels.Ensure(client, "appmodule", app.Id, "name", SiteMapCatalogo.AppName))
                {
                    Console.WriteLine($"[4k]   app: nombre (3082) = '{SiteMapCatalogo.AppName.Es}'.");
                    appTouched = true; touched = true;
                }
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
            {
                // Verify (SiteMapAssertions) falla si el 3082 no quedó: acá no se corta el resto del deploy.
                Console.Error.WriteLine($"[4k]   WARN: no se pudo fijar el nombre 3082 de la app: {ex.Message}");
            }

            if (!appTouched)
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
