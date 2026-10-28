// Asserts E2E de IconsSpec.cs: los web resources SVG existen (tipo 11), con el contenido de los .svg versionados y como
// componentes de smlt_core_smelter; cada tabla de Smelter tiene su IconVectorName; la app usa el web resource del ícono de
// app. (Los VectorIcon del sitemap los verifica SiteMapAssertions.) Solo lee.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class IconsAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[ICONS] Verificando web resources SVG, IconVectorName de las tablas y ícono de la app (IconsSpec.cs) ...");

        var ids = new Dictionary<string, Guid>();
        foreach (var icon in IconsCatalogo.All)
        {
            var wr = IconsProvisioner.FindWebResource(client, icon.WebResourceName, "webresourceid", "content", "webresourcetype");
            Assert(wr is not null, $"no existe el web resource '{icon.WebResourceName}' (¿corriste Smelter.Deploy?)");
            Assert(wr!.GetAttributeValue<OptionSetValue>("webresourcetype")?.Value == IconsCatalogo.SvgWebResourceType,
                $"'{icon.WebResourceName}' no es de tipo SVG (11)");
            Assert(!IconsCatalogo.NeedsContentUpdate(wr.GetAttributeValue<string>("content"), IconsCatalogo.ToBase64(IconsCatalogo.ReadSvg(icon))),
                $"el contenido de '{icon.WebResourceName}' difiere del .svg versionado");
            Assert(IconsProvisioner.IsInSolution(client, wr.Id),
                $"'{icon.WebResourceName}' no es componente de {Catalogo.SolutionName}: no viajaría a Test/Prod");
            ids[icon.WebResourceName] = wr.Id;
        }

        foreach (var spec in IconsCatalogo.TableIcons)
        {
            if (spec.Table == HmacKeyTableSpec.Table && !HmacKeyTableProvisioner.TableExists(client))
                continue;
            var entity = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
            {
                LogicalName = spec.Table,
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true,
            })).EntityMetadata;
            Assert(entity.IconVectorName == spec.Icon.WebResourceName,
                $"{spec.Table}: IconVectorName '{entity.IconVectorName}', esperado '{spec.Icon.WebResourceName}'");
        }

        var app = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("webresourceid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, MdaSpec.AppUniqueName) } },
        }).Entities.SingleOrDefault();
        Assert(app is not null, $"sin app '{MdaSpec.AppUniqueName}'");
        Assert(app!.GetAttributeValue<Guid>("webresourceid") == ids[IconsCatalogo.App.WebResourceName],
            $"la app no usa el web resource '{IconsCatalogo.App.WebResourceName}' como ícono");

        Console.WriteLine($"[ICONS] OK: {IconsCatalogo.All.Count} SVG en la solución, tablas con IconVectorName e ícono de app.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[ICONS]: " + message);
    }
}
