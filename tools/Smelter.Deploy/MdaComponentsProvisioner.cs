// Provisioner idempotente del pin de forms y vistas en la MDA (spec: MdaComponentsSpec.cs).
// Solo agrega lo que falta (AddAppComponentsRequest) y publica la app. Re-correr es un no-op.
// OJO: appmodulecomponent.appmoduleidunique es OTRO Guid distinto de appmodule.appmoduleid.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class MdaComponentsProvisioner
{
    internal static void Run(ServiceClient client)
    {
        Console.WriteLine($"[4g] Pin de forms y vistas en la app {MdaSpec.AppUniqueName} ...");
        var app = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("appmoduleid", "appmoduleidunique"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, MdaSpec.AppUniqueName) } },
        }).Entities.SingleOrDefault();
        if (app is null)
        {
            Console.Error.WriteLine($"[4g]   WARN: app '{MdaSpec.AppUniqueName}' no encontrada — se omite el pin.");
            return;
        }

        var pinned = client.RetrieveMultiple(new QueryExpression("appmodulecomponent")
        {
            ColumnSet = new ColumnSet("objectid"),
            Criteria = { Conditions = { new ConditionExpression("appmoduleidunique", ConditionOperator.Equal, app.GetAttributeValue<Guid>("appmoduleidunique")) } },
        }).Entities.Select(e => e.GetAttributeValue<Guid>("objectid")).ToHashSet();

        var missing = MdaComponentsCatalogo.Missing(MdaComponentsCatalogo.All, pinned);
        if (missing.Count == 0)
        {
            Console.WriteLine($"[4g]   OK: los {MdaComponentsCatalogo.All.Count} componentes ya estaban pineados — nada que agregar.");
            return;
        }

        var components = new EntityReferenceCollection();
        foreach (var c in missing)
        {
            components.Add(new EntityReference(MdaComponentsCatalogo.EntityName(c.ComponentType), c.Id));
            Console.WriteLine($"[4g]   + {c.Table}: {c.Name} ({c.Kind})");
        }
        client.Execute(new AddAppComponentsRequest { AppId = app.Id, Components = components });

        // PublishXml acepta la app por id; si el servidor no lo reconoce se reintenta con el unique name.
        try { client.Execute(new PublishXmlRequest { ParameterXml = MdaComponentsCatalogo.PublishAppXml(app.Id.ToString()) }); }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
        {
            client.Execute(new PublishXmlRequest { ParameterXml = MdaComponentsCatalogo.PublishAppXml(MdaSpec.AppUniqueName) });
        }
        Console.WriteLine($"[4g]   {missing.Count} componente(s) agregados y app publicada.");
    }
}
