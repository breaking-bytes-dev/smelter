// [MDA] Salud de la model-driven app de Smelter a nivel METADATA, solo lectura, por SDK:
//   1. ValidateAppRequest sin errores (los warnings se reportan).
//   2. Sitemap/componentes: subareas -> tablas existentes, dashboards existentes, cada tabla de la app
//      con >= 1 main form activo, vistas/forms referenciados existentes, hmackey FUERA de app y sitemap.
//   3. Forms y vistas de las 6 tablas no referencian atributos inexistentes (p.ej. los 6 eliminados en 1.3.0.0).
//   4. Web resources (libraries de forms y JS del command bar) existentes.
// Cada check imprime "[MDA] OK" o "[MDA] FAIL <qué> @ <dónde>"; los FAIL se acumulan y al final lanzan.
// El parseo puro vive en Smelter.Deploy/MdaXml.cs (testeado en Contract tests).

using System.IO.Compression;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class MdaAssertions
{
    private const string PublisherPrefix = "sanic_";
    private static readonly string[] SmelterTables = SecuritySpec.Tables.All.ToArray();

    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine($"[MDA] Verificando la app {MdaSpec.AppUniqueName} (solo lectura) ...");
        var fails = new List<string>();

        var app = client.RetrieveMultiple(new QueryExpression("appmodule")
        {
            ColumnSet = new ColumnSet("appmoduleid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, MdaSpec.AppUniqueName) } },
        }).Entities.SingleOrDefault();
        if (app is null) throw new InvalidOperationException($"ASSERT[MDA]: no existe la app {MdaSpec.AppUniqueName}");
        var appId = app.Id;

        var tables = RetrieveTables(client, SmelterTables.Append(HmacKeyTableSpec.Table).ToArray());
        var components = client.RetrieveMultiple(new QueryExpression("appmodulecomponent")
        {
            ColumnSet = new ColumnSet("componenttype", "objectid"),
            Criteria = { Conditions = { new ConditionExpression("appmoduleidunique", ConditionOperator.Equal, appId) } },
        }).Entities.Select(e => (Type: e.GetAttributeValue<OptionSetValue>("componenttype")?.Value ?? -1,
                                 Id: e.GetAttributeValue<Guid>("objectid"))).ToList();

        Check(fails, "1 ValidateApp", () => CheckValidateApp(client, appId));
        Check(fails, "2 componentes+sitemap", () => CheckAppStructure(client, tables, components));
        Check(fails, "3 forms/vistas->atributos", () => CheckAttributes(client, tables));
        Check(fails, "4 web resources", () => CheckWebResources(client, tables));

        if (fails.Count > 0)
            throw new InvalidOperationException("ASSERT[MDA]: " + fails.Count + " check(s) fallidos (ver [MDA] FAIL arriba).");
        Console.WriteLine("[MDA] OK: app válida, sitemap/forms/vistas/web resources consistentes.");
    }

    // ── 1 ──────────────────────────────────────────────────────────────────
    private static List<string> CheckValidateApp(ServiceClient client, Guid appId)
    {
        var resp = (ValidateAppResponse)client.Execute(new ValidateAppRequest { AppModuleId = appId });
        var issues = resp.AppValidationResponse?.ValidationIssueList ?? Array.Empty<ValidationIssue>();
        foreach (var w in issues.Where(i => i.ErrorType == ErrorType.Warning))
            Console.WriteLine($"[MDA] WARN ValidateApp: {w.Message} @ {w.DisplayName ?? w.ComponentId.ToString()}");
        return issues.Where(i => i.ErrorType == ErrorType.Error)
            .Select(i => $"ValidateApp: {i.Message} @ {i.DisplayName ?? i.ComponentId.ToString()}").ToList();
    }

    // ── 2 ──────────────────────────────────────────────────────────────────
    private static List<string> CheckAppStructure(ServiceClient client, Dictionary<string, EntityMetadata> tables,
        List<(int Type, Guid Id)> components)
    {
        var errors = new List<string>();
        var byId = tables.Values.Where(t => t.MetadataId.HasValue).ToDictionary(t => t.MetadataId!.Value);

        var appTables = new List<string>();
        foreach (var c in components.Where(c => c.Type == MdaSpec.ComponentEntity))
        {
            if (byId.TryGetValue(c.Id, out var t)) appTables.Add(t.LogicalName);
            else errors.Add($"componente entidad {c.Id} @ app: no es una tabla Smelter conocida ni existe como hmackey");
        }
        if (appTables.Contains(HmacKeyTableSpec.Table))
            errors.Add($"{HmacKeyTableSpec.Table} @ app: la tabla de keys NO debe estar en la app");
        foreach (var missing in SmelterTables.Except(appTables))
            errors.Add($"{missing} @ app: tabla Smelter ausente de la app");

        // Cada tabla de la app: >= 1 main form activo (type=2, formactivationstate=1).
        foreach (var table in appTables.Where(t => t != HmacKeyTableSpec.Table))
        {
            var mains = client.RetrieveMultiple(new QueryExpression("systemform")
            {
                ColumnSet = new ColumnSet(false),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("objecttypecode", ConditionOperator.Equal, table),
                        new ConditionExpression("type", ConditionOperator.Equal, 2),
                        new ConditionExpression("formactivationstate", ConditionOperator.Equal, 1),
                    },
                },
            }).Entities.Count;
            if (mains == 0) errors.Add($"sin main form activo @ {table}");
        }

        // Vistas y forms referenciados explícitamente por la app existen.
        ExistAll(client, "savedquery", "savedqueryid", components.Where(c => c.Type == MdaSpec.ComponentView).Select(c => c.Id),
            id => errors.Add($"vista {id} @ app: no existe"));
        ExistAll(client, "systemform", "formid", components.Where(c => c.Type == MdaSpec.ComponentForm).Select(c => c.Id),
            id => errors.Add($"form {id} @ app: no existe"));

        // Sitemap.
        var siteMapId = components.Where(c => c.Type == MdaSpec.ComponentSiteMap).Select(c => (Guid?)c.Id).FirstOrDefault();
        if (siteMapId is null) { errors.Add("sin sitemap @ app"); return errors; }
        var siteMapXml = client.Retrieve("sitemap", siteMapId.Value, new ColumnSet("sitemapxml")).GetAttributeValue<string>("sitemapxml");
        var subAreas = MdaXml.SiteMapSubAreas(siteMapXml);

        var entities = subAreas.Where(s => s.Entity is not null).Select(s => s.Entity!).Distinct().ToArray();
        var existing = RetrieveTables(client, entities);
        foreach (var e in entities.Where(e => !existing.ContainsKey(e)))
            errors.Add($"subarea -> tabla inexistente '{e}' @ sitemap");
        if (entities.Contains(HmacKeyTableSpec.Table))
            errors.Add($"{HmacKeyTableSpec.Table} @ sitemap: la tabla de keys NO debe estar en el sitemap");
        foreach (var e in entities.Where(existing.ContainsKey).Except(appTables))
            errors.Add($"subarea '{e}' @ sitemap: la tabla no es componente de la app");

        var dashboards = subAreas.Where(s => s.Url is not null).Select(s => MdaXml.DashboardId(s.Url!)).Where(g => g.HasValue).Select(g => g!.Value);
        ExistAll(client, "systemform", "formid", dashboards, id => errors.Add($"dashboard {id} @ sitemap: no existe"));
        return errors;
    }

    // ── 3 ──────────────────────────────────────────────────────────────────
    private static List<string> CheckAttributes(ServiceClient client, Dictionary<string, EntityMetadata> tables)
    {
        var errors = new List<string>();
        var deleted = SchemaCleanupCatalogo.Attributes.Select(a => a.AttributeLogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var table in SmelterTables)
        {
            if (!tables.TryGetValue(table, out var meta)) { errors.Add($"tabla inexistente @ {table}"); continue; }
            var known = (meta.Attributes ?? Array.Empty<AttributeMetadata>()).Select(a => a.LogicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            void Report(string kind, string where, IEnumerable<string> referenced)
            {
                foreach (var a in referenced.Where(a => !known.Contains(a)).OrderBy(a => a))
                    errors.Add($"{kind} referencia atributo inexistente '{a}'{(deleted.Contains(a) ? " (eliminado en 1.3.0.0)" : "")} @ {table}/{where}");
            }

            foreach (var f in client.RetrieveMultiple(new QueryExpression("systemform")
            {
                ColumnSet = new ColumnSet("name", "formxml"),
                Criteria = { Conditions = { new ConditionExpression("objecttypecode", ConditionOperator.Equal, table) } },
            }).Entities)
            {
                var xml = f.GetAttributeValue<string>("formxml");
                if (!string.IsNullOrWhiteSpace(xml)) Report("form", f.GetAttributeValue<string>("name") ?? f.Id.ToString(), MdaXml.FormAttributes(xml));
            }

            foreach (var v in client.RetrieveMultiple(new QueryExpression("savedquery")
            {
                ColumnSet = new ColumnSet("name", "fetchxml", "layoutxml"),
                Criteria = { Conditions = { new ConditionExpression("returnedtypecode", ConditionOperator.Equal, table) } },
            }).Entities)
            {
                var name = v.GetAttributeValue<string>("name") ?? v.Id.ToString();
                var fetch = v.GetAttributeValue<string>("fetchxml");
                var layout = v.GetAttributeValue<string>("layoutxml");
                if (!string.IsNullOrWhiteSpace(fetch)) Report("vista(fetch)", name, MdaXml.FetchAttributes(fetch).Attributes);
                if (!string.IsNullOrWhiteSpace(layout)) Report("vista(layout)", name, MdaXml.LayoutAttributes(layout));
            }
        }
        return errors;
    }

    // ── 4 ──────────────────────────────────────────────────────────────────
    private static List<string> CheckWebResources(ServiceClient client, Dictionary<string, EntityMetadata> tables)
    {
        var errors = new List<string>();
        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // web resource -> dónde

        foreach (var table in SmelterTables)
        {
            foreach (var f in client.RetrieveMultiple(new QueryExpression("systemform")
            {
                ColumnSet = new ColumnSet("name", "formxml"),
                Criteria = { Conditions = { new ConditionExpression("objecttypecode", ConditionOperator.Equal, table) } },
            }).Entities)
            {
                var xml = f.GetAttributeValue<string>("formxml");
                if (string.IsNullOrWhiteSpace(xml)) continue;
                foreach (var wr in MdaXml.FormWebResources(xml)) refs.TryAdd(wr, $"{table}/form {f.GetAttributeValue<string>("name")}");
            }

            try
            {
                var ribbon = (RetrieveEntityRibbonResponse)client.Execute(new RetrieveEntityRibbonRequest
                {
                    EntityName = table,
                    RibbonLocationFilter = RibbonLocationFilters.All,
                });
                using var zip = new ZipArchive(new MemoryStream(ribbon.CompressedEntityXml));
                var entry = zip.GetEntry("RibbonXml.xml");
                if (entry is null) { Console.WriteLine($"[MDA] WARN ribbon de {table} sin RibbonXml.xml — no se verifica su JS"); continue; }
                using var reader = new StreamReader(entry.Open());
                foreach (var wr in MdaXml.WebResourceRefs(reader.ReadToEnd())) refs.TryAdd(wr, $"{table}/command bar");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MDA] WARN no se pudo leer el ribbon de {table}: {ex.Message}");
            }
        }

        // Solo los propios (prefijo del publisher): los del producto Microsoft no son nuestra responsabilidad.
        var own = refs.Where(r => r.Key.StartsWith(PublisherPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (own.Count == 0) return errors;

        var found = client.RetrieveMultiple(new QueryExpression("webresource")
        {
            ColumnSet = new ColumnSet("name"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.In, own.Select(o => o.Key).ToArray()) } },
        }).Entities.Select(e => e.GetAttributeValue<string>("name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var o in own.Where(o => !found.Contains(o.Key)))
            errors.Add($"web resource inexistente '{o.Key}' @ {o.Value}");
        Console.WriteLine($"[MDA]   web resources propios referenciados: {own.Count}");
        return errors;
    }

    // ── helpers ────────────────────────────────────────────────────────────
    private static void Check(List<string> fails, string name, Func<List<string>> run)
    {
        try
        {
            var errors = run();
            if (errors.Count == 0) { Console.WriteLine($"[MDA] OK {name}"); return; }
            foreach (var e in errors) Console.WriteLine($"[MDA] FAIL {name}: {e}");
            fails.AddRange(errors);
        }
        catch (Exception ex)
        {
            var msg = $"{name}: excepción {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"[MDA] FAIL {msg}");
            fails.Add(msg);
        }
    }

    private static void ExistAll(ServiceClient client, string entity, string idColumn, IEnumerable<Guid> ids, Action<Guid> onMissing)
    {
        var wanted = ids.Distinct().ToArray();
        if (wanted.Length == 0) return;
        var found = client.RetrieveMultiple(new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(idColumn),
            Criteria = { Conditions = { new ConditionExpression(idColumn, ConditionOperator.In, wanted.Cast<object>().ToArray()) } },
        }).Entities.Select(e => e.Id).ToHashSet();
        foreach (var id in wanted.Where(id => !found.Contains(id))) onMissing(id);
    }

    /// <summary>Metadata (con atributos) de las tablas pedidas que existan; las inexistentes simplemente no aparecen.</summary>
    private static Dictionary<string, EntityMetadata> RetrieveTables(ServiceClient client, string[] logicalNames)
    {
        if (logicalNames.Length == 0) return new();
        var resp = (RetrieveMetadataChangesResponse)client.Execute(new RetrieveMetadataChangesRequest
        {
            Query = new EntityQueryExpression
            {
                Criteria = new MetadataFilterExpression(LogicalOperator.And)
                {
                    Conditions = { new MetadataConditionExpression("LogicalName", MetadataConditionOperator.In, logicalNames) },
                },
                Properties = new MetadataPropertiesExpression("LogicalName", "MetadataId", "Attributes"),
                AttributeQuery = new AttributeQueryExpression
                {
                    Properties = new MetadataPropertiesExpression("LogicalName"),
                },
            },
        });
        return resp.EntityMetadata.ToDictionary(e => e.LogicalName, StringComparer.OrdinalIgnoreCase);
    }
}
