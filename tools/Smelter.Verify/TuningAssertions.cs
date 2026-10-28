// Asserts E2E de los ajustes de SchemaTuningSpec.cs: required level / default de las columnas ajustadas y campos
// ocultos en los main forms. Solo lee metadata.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class TuningAssertions
{
    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[TUNE] Verificando ajustes de columnas y forms (SchemaTuningSpec.cs) ...");

        foreach (var spec in SchemaTuningCatalogo.Attributes)
        {
            var attribute = ((RetrieveAttributeResponse)client.Execute(new RetrieveAttributeRequest
            {
                EntityLogicalName = spec.Entity,
                LogicalName = spec.Attribute,
                RetrieveAsIfPublished = true,
            })).AttributeMetadata;

            if (spec.RequiredLevel is { } level)
                Assert(attribute.RequiredLevel?.Value == level,
                    $"{spec.Attribute}: required level {attribute.RequiredLevel?.Value}, esperado {level}");
            if (spec.PicklistDefault is { } pick)
                Assert((attribute as PicklistAttributeMetadata)?.DefaultFormValue == pick,
                    $"{spec.Attribute}: default {(attribute as PicklistAttributeMetadata)?.DefaultFormValue}, esperado {pick}");
            if (spec.BooleanDefault is { } flag)
                Assert((attribute as BooleanAttributeMetadata)?.DefaultValue == flag,
                    $"{spec.Attribute}: default {(attribute as BooleanAttributeMetadata)?.DefaultValue}, esperado {flag}");
            if (spec.IntMin is { } iMin)
                Assert((attribute as IntegerAttributeMetadata)?.MinValue == iMin,
                    $"{spec.Attribute}: MinValue {(attribute as IntegerAttributeMetadata)?.MinValue}, esperado {iMin}");
            if (spec.IntMax is { } iMax)
                Assert((attribute as IntegerAttributeMetadata)?.MaxValue == iMax,
                    $"{spec.Attribute}: MaxValue {(attribute as IntegerAttributeMetadata)?.MaxValue}, esperado {iMax}");
            Console.WriteLine($"[TUNE]   {spec.Entity}.{spec.Attribute}: OK.");
        }

        // Comportamiento, no solo metadata: guardar un Chunk Size fuera de rango debe RECHAZARSE (antes un 50 se aceptaba y el motor lo acotaba).
        foreach (var outOfRange in new[] { SchemaTuningCatalogo.ChunkSizeMin - 1, 50, SchemaTuningCatalogo.ChunkSizeMax + 1 })
        {
            Guid? leaked = null;
            try
            {
                leaked = client.Create(new Microsoft.Xrm.Sdk.Entity(SecuritySpec.Tables.ImportTemplate)
                {
                    ["sanic_smlt_it_name"] = "Smelter Verify · chunk-range probe",
                    ["sanic_smlt_it_chunksize"] = outOfRange,
                });
            }
            catch (Exception ex)
            {
                // esperado: la columna rechaza el valor (el SDK lo informa como fault o como DataverseOperationException).
                Console.WriteLine($"[TUNE]   chunk size {outOfRange}: rechazado ({ex.Message.Split('\n')[0]}).");
            }
            if (leaked is { } id)
            {
                client.Delete(SecuritySpec.Tables.ImportTemplate, id);
                Assert(false, $"sanic_smlt_it_chunksize={outOfRange} debía rechazarse y se guardó (¿MinValue/MaxValue sin aplicar?)");
            }
        }
        Console.WriteLine("[TUNE]   Chunk Size fuera de rango (<100 / >10000): rechazado OK.");

        foreach (var hidden in SchemaTuningCatalogo.HiddenFields)
        {
            var mains = client.RetrieveMultiple(new QueryExpression("systemform")
            {
                ColumnSet = new ColumnSet("name", "formxml"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("objecttypecode", ConditionOperator.Equal, hidden.Entity),
                        new ConditionExpression("type", ConditionOperator.Equal, 2),
                        new ConditionExpression("formactivationstate", ConditionOperator.Equal, 1),
                    },
                },
            }).Entities;
            Assert(mains.Count > 0, $"sin main form activo @ {hidden.Entity}");

            foreach (var form in mains)
            {
                var doc = System.Xml.Linq.XDocument.Parse(form.GetAttributeValue<string>("formxml"));
                var cell = doc.Descendants("control")
                    .FirstOrDefault(c => string.Equals((string?)c.Attribute("datafieldname"), hidden.Field, StringComparison.OrdinalIgnoreCase))
                    ?.Ancestors("cell").FirstOrDefault();
                if (cell is null) continue; // el campo no está en este form: nada que ocultar
                var isHidden = string.Equals((string?)cell.Attribute("visible"), "false", StringComparison.OrdinalIgnoreCase);
                Assert(isHidden == !hidden.Visible,
                    $"el form '{form.GetAttributeValue<string>("name")}': {hidden.Field} " +
                    $"{(isHidden ? "oculto" : "visible")}, esperado {(hidden.Visible ? "visible" : "oculto")}");
            }
            Console.WriteLine($"[TUNE]   {hidden.Entity}.{hidden.Field}: {(hidden.Visible ? "visible" : "oculto")} en el main form OK.");
        }

        Console.WriteLine("[TUNE] OK: ajustes según spec.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[TUNE]: " + message);
    }
}
