// Asserts E2E de las columnas nuevas provisionadas por SchemaAdditionsProvisioner (SchemaAdditionsSpec.cs):
// existen con el tipo/precisión/default del spec, son componentes de smlt_core_smelter (viajan a Test/Prod) y,
// si la spec lo pide, el control está en el main form de la tabla. Solo lee metadata.

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;

namespace Smelter.Verify;

internal static class AdditionsAssertions
{
    private const int AttributeComponentType = 2;

    internal static void RunAll(ServiceClient client)
    {
        Console.WriteLine("[ADD] Verificando columnas nuevas (SchemaAdditionsSpec.cs) ...");

        var solutionId = client.RetrieveMultiple(new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, SchemaAdditionsCatalogo.SolutionUniqueName) } },
        }).Entities.SingleOrDefault()?.Id;
        Assert(solutionId is not null, $"no existe la solución {SchemaAdditionsCatalogo.SolutionUniqueName}");

        foreach (var spec in SchemaAdditionsCatalogo.Columns)
        {
            var attribute = ((RetrieveAttributeResponse)client.Execute(new RetrieveAttributeRequest
            {
                EntityLogicalName = spec.EntityLogicalName,
                LogicalName = spec.SchemaName,
                RetrieveAsIfPublished = true,
            })).AttributeMetadata;

            switch (spec.Kind)
            {
                case SchemaColumnKind.Decimal:
                    var dec = attribute as DecimalAttributeMetadata;
                    Assert(dec is not null, $"{spec.SchemaName} debía ser Decimal, es {attribute.AttributeType}");
                    Assert(dec!.Precision == SchemaAdditionsCatalogo.ByteCursorPrecision,
                        $"{spec.SchemaName}: precisión {dec.Precision}, esperada {SchemaAdditionsCatalogo.ByteCursorPrecision}");
                    Assert(dec.MaxValue >= SchemaAdditionsCatalogo.ByteCursorMax,
                        $"{spec.SchemaName}: MaxValue {dec.MaxValue} < {SchemaAdditionsCatalogo.ByteCursorMax} (no alcanza para un File de 10 GB)");
                    break;
                case SchemaColumnKind.Boolean:
                    var boolean = attribute as BooleanAttributeMetadata;
                    Assert(boolean is not null, $"{spec.SchemaName} debía ser Boolean, es {attribute.AttributeType}");
                    Assert(boolean!.DefaultValue == SchemaAdditionsCatalogo.InsertOnlyDefault,
                        $"{spec.SchemaName}: default {boolean.DefaultValue}, esperado {SchemaAdditionsCatalogo.InsertOnlyDefault}");
                    break;
            }

            var inSolution = client.RetrieveMultiple(new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("objectid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId!.Value),
                        new ConditionExpression("componenttype", ConditionOperator.Equal, AttributeComponentType),
                        new ConditionExpression("objectid", ConditionOperator.Equal, attribute.MetadataId!.Value),
                    },
                },
            }).Entities.Count > 0;
            Assert(inSolution, $"{spec.SchemaName} no es componente de {SchemaAdditionsCatalogo.SolutionUniqueName}: no viajaría a Test/Prod");

            if (spec.Form is not null)
            {
                var mains = client.RetrieveMultiple(new QueryExpression("systemform")
                {
                    ColumnSet = new ColumnSet("name", "formxml"),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression("objecttypecode", ConditionOperator.Equal, spec.EntityLogicalName),
                            new ConditionExpression("type", ConditionOperator.Equal, 2),
                            new ConditionExpression("formactivationstate", ConditionOperator.Equal, 1),
                        },
                    },
                }).Entities;
                Assert(mains.Count > 0, $"sin main form activo @ {spec.EntityLogicalName}");
                foreach (var form in mains)
                    Assert(MdaXml.FormAttributes(form.GetAttributeValue<string>("formxml")).Contains(spec.SchemaName, StringComparer.OrdinalIgnoreCase),
                        $"el main form '{form.GetAttributeValue<string>("name")}' no tiene el control de {spec.SchemaName}");
            }

            Console.WriteLine($"[ADD]   {spec.EntityLogicalName}.{spec.SchemaName}: OK.");
        }

        Console.WriteLine($"[ADD] OK: {SchemaAdditionsCatalogo.Columns.Length} columna(s) según spec.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ASSERT[ADD]: " + message);
    }
}
