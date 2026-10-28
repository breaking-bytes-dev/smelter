// Provisión idempotente de los íconos de Smelter (IconsSpec.cs) por SDK:
//   1. Web resources SVG (webresourcetype 11) DENTRO de smlt_core_smelter: se crean con SolutionUniqueName; si ya existen se
//      actualiza el content solo si cambió (compara base64) y se agrega a la solución si no era componente.
//   2. EntityMetadata.IconVectorName de cada tabla de Smelter (UpdateEntityRequest sobre la metadata recuperada: la tabla
//      se toca SOLO por esta propiedad).
//   3. appmodule.webresourceid = el web resource del ícono de la app.
// Publica los web resources, las tablas y la app que tocó. No destructivo.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class IconsProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine("[4m] IconsProvisioner: web resources SVG, IconVectorName de las tablas y ícono de la app (IconsSpec.cs) ...");

        var touchedResources = new List<Guid>();
        var ids = new Dictionary<string, Guid>();
        foreach (var icon in IconsCatalogo.All)
        {
            var (id, touched) = UpsertWebResource(client, icon);
            ids[icon.WebResourceName] = id;
            if (touched) touchedResources.Add(id);
            EnsureInSolution(client, id, icon);
        }

        var touchedTables = new List<string>();
        foreach (var spec in IconsCatalogo.TableIcons)
        {
            if (spec.Table == HmacKeyTableSpec.Table && !HmacKeyTableProvisioner.TableExists(client))
            {
                Console.WriteLine($"[4m]   {spec.Table}: la tabla no existe todavía — se omite el ícono.");
                continue;
            }

            var entity = ((RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
            {
                LogicalName = spec.Table,
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true,
            })).EntityMetadata;

            if (string.Equals(entity.IconVectorName, spec.Icon.WebResourceName, StringComparison.Ordinal))
                continue;

            entity.IconVectorName = spec.Icon.WebResourceName;
            client.Execute(new UpdateEntityRequest { Entity = entity, MergeLabels = true });
            Console.WriteLine($"[4m]   {spec.Table}: IconVectorName = {spec.Icon.WebResourceName}.");
            touchedTables.Add(spec.Table);
        }

        string? appKey = null;
        var app = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("appmoduleid", "webresourceid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, MdaSpec.AppUniqueName) } },
        }).Entities.SingleOrDefault();
        if (app is null)
        {
            Console.Error.WriteLine($"[4m]   WARN: app '{MdaSpec.AppUniqueName}' no encontrada — se omite el ícono de la app.");
        }
        else if (app.GetAttributeValue<Guid>("webresourceid") != ids[IconsCatalogo.App.WebResourceName])
        {
            client.Update(new Entity("appmodule", app.Id) { ["webresourceid"] = ids[IconsCatalogo.App.WebResourceName] });
            Console.WriteLine($"[4m]   app: ícono = {IconsCatalogo.App.WebResourceName}.");
            appKey = app.Id.ToString();
        }
        else
        {
            Console.WriteLine("[4m]   app: ícono ya al día — nada que hacer.");
        }

        if (touchedResources.Count + touchedTables.Count == 0 && appKey is null)
        {
            Console.WriteLine($"[4m] Icons OK: los {IconsCatalogo.All.Count} SVG y sus usos ya estaban al día — nada que hacer.");
            return;
        }

        client.Execute(new PublishXmlRequest { ParameterXml = IconsCatalogo.PublishXml(touchedResources, touchedTables, appKey) });
        Console.WriteLine($"[4m] Icons OK: {touchedResources.Count} web resource(s), {touchedTables.Count} tabla(s){(appKey is null ? "" : " y la app")} publicados.");
    }

    internal static Entity? FindWebResource(IOrganizationService service, string name, params string[] columns) =>
        service.RetrieveMultiple(new QueryExpression("webresource")
        {
            ColumnSet = new ColumnSet(columns),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, name) } },
        }).Entities.SingleOrDefault();

    private static (Guid Id, bool Touched) UpsertWebResource(ServiceClient client, IconSpec icon)
    {
        var contentB64 = IconsCatalogo.ToBase64(IconsCatalogo.ReadSvg(icon));
        var existing = FindWebResource(client, icon.WebResourceName, "webresourceid", "content", "webresourcetype");

        if (existing is not null)
        {
            if (!IconsCatalogo.NeedsContentUpdate(existing.GetAttributeValue<string>("content"), contentB64))
            {
                Console.WriteLine($"[4m]   web resource '{icon.WebResourceName}' ya al día.");
                return (existing.Id, false);
            }
            client.Update(new Entity("webresource", existing.Id) { ["content"] = contentB64 });
            Console.WriteLine($"[4m]   web resource '{icon.WebResourceName}' existente — content actualizado.");
            return (existing.Id, true);
        }

        var create = new CreateRequest
        {
            Target = new Entity("webresource")
            {
                ["name"] = icon.WebResourceName,
                ["displayname"] = icon.DisplayName,
                ["webresourcetype"] = new OptionSetValue(IconsCatalogo.SvgWebResourceType),
                ["content"] = contentB64,
            },
        };
        create.Parameters["SolutionUniqueName"] = Catalogo.SolutionName;
        var id = ((CreateResponse)client.Execute(create)).id;
        Console.WriteLine($"[4m]   web resource '{icon.WebResourceName}' creado: {id}.");
        return (id, true);
    }

    // Un web resource preexistente (creado fuera de la solución) se agrega a smlt_core_smelter.
    private static void EnsureInSolution(ServiceClient client, Guid webResourceId, IconSpec icon)
    {
        if (IsInSolution(client, webResourceId))
            return;

        client.Execute(new AddSolutionComponentRequest
        {
            ComponentId = webResourceId,
            ComponentType = IconsCatalogo.WebResourceComponentType,
            SolutionUniqueName = Catalogo.SolutionName,
            AddRequiredComponents = false,
        });
        Console.WriteLine($"[4m]   web resource '{icon.WebResourceName}' AGREGADO a '{Catalogo.SolutionName}'.");
    }

    internal static bool IsInSolution(IOrganizationService service, Guid webResourceId)
    {
        var solutionId = service.RetrieveMultiple(new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, Catalogo.SolutionName) } },
        }).Entities.SingleOrDefault()?.Id
            ?? throw new InvalidOperationException($"Solución '{Catalogo.SolutionName}' no encontrada.");

        return service.RetrieveMultiple(new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("objectid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId),
                    new ConditionExpression("componenttype", ConditionOperator.Equal, IconsCatalogo.WebResourceComponentType),
                    new ConditionExpression("objectid", ConditionOperator.Equal, webResourceId),
                },
            },
        }).Entities.Count > 0;
    }
}
