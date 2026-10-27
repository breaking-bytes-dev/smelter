// Pasos [E2E-1], [E2E-2], [E2E-2g], [E2E-3], [E2E-8], [E2E-12]: escritura de datos y su contenido. Ver E2eRunner.cs.

using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using static Smelter.Verify.ApiCalls;

namespace Smelter.Verify;

internal static partial class E2eRunner
{
    // ─── [E2E-1] CreateIfNotExists = false ────────────────────────────────────────────────────────────

    private static void CreateIfNotExistsFalse(Env env)
    {
        var c = E2eSpec.CreateIfNotExists.Case();
        foreach (var email in E2eSpec.CreateIfNotExists.ExistingEmails)
            env.Client.Create(new Entity("contact")
            {
                ["emailaddress1"] = email, ["firstname"] = E2eSpec.CreateIfNotExists.OldFirstName, ["lastname"] = "Pre",
            });

        var jobId = CreateJob(env, ImportTemplate(env, c.Dto), c.Key, c.Csv);
        RunAndAssert(env, jobId, new JobExpect(ImportJobStatus.Completed, Total: 3, Success: 2, Failed: 0, Skipped: 1));

        foreach (var row in c.Rows.Where(r => E2eSpec.CreateIfNotExists.ExistingEmails.Contains(r.Email)))
        {
            var contact = RequireContact(env, row.Email, "firstname");
            Ensure(contact.GetAttributeValue<string>("firstname") == row.First,
                $"{row.Email}: existía y UpdateIfExists=true: firstname debía actualizarse a '{row.First}', es '{contact.GetAttributeValue<string>("firstname")}'");
        }
        Ensure(ContactByEmail(env, E2eSpec.CreateIfNotExists.MissingEmail) is null,
            $"{E2eSpec.CreateIfNotExists.MissingEmail}: no existía y CreateIfNotExists=false: NO debía crearse");
        Console.WriteLine("[E2E-1]   2 existentes actualizados, 1 inexistente Skipped y sin crear.");
    }

    // ─── [E2E-2] tipos de dato ───────────────────────────────────────────────────────────────────────────

    private static bool Writable(AttributeMetadata a) => a.IsValidForCreate == true && a.IsValidForUpdate == true;

    private static void DataTypes(Env env)
    {
        // Lo que el Dev ofrece (metadata de contact) decide las columnas opcionales: nada se inventa ni se asume.
        var meta = (RetrieveEntityResponse)env.Client.Execute(new RetrieveEntityRequest
        {
            LogicalName = "contact", EntityFilters = EntityFilters.Attributes, RetrieveAsIfPublished = false,
        });
        var attrs = meta.EntityMetadata.Attributes.ToDictionary(a => a.LogicalName, a => a, StringComparer.OrdinalIgnoreCase);

        var decimalColumn = attrs.Values.OfType<DecimalAttributeMetadata>()
            .Where(a => Writable(a) && string.IsNullOrEmpty(a.AttributeOf) && (a.Precision ?? 0) >= 1
                        && !a.LogicalName.Equals("exchangerate", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.LogicalName).OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
        var includeSeen = attrs.TryGetValue("lastusedincampaign", out var seen) && seen is DateTimeAttributeMetadata && Writable(seen);

        foreach (var required in new[] { "numberofchildren", "creditlimit", "transactioncurrencyid", "birthdate", "donotemail",
                     "preferredcontactmethodcode", "gendercode", "parentcustomerid" })
            Ensure(attrs.TryGetValue(required, out var a) && Writable(a),
                $"contact.{required} debía existir y ser escribible en este entorno (el paso usa columnas estándar de contact)");

        if (decimalColumn is null)
            Console.WriteLine("[E2E-2]   SKIP Decimal: contact no tiene ninguna columna Decimal escribible en este entorno " +
                              "(el tipo Decimal queda cubierto solo por los tests unitarios de TypeCoercer).");
        else
            Console.WriteLine($"[E2E-2]   Decimal sobre contact.{decimalColumn}.");
        if (!includeSeen)
            Console.WriteLine("[E2E-2]   SKIP DateTime con hora: contact.lastusedincampaign no es escribible; solo se prueba birthdate (DateOnly).");

        var currencyId = env.Client.RetrieveMultiple(new QueryExpression("organization")
        {
            ColumnSet = new ColumnSet("basecurrencyid"), TopCount = 1,
        }).Entities.First().GetAttributeValue<EntityReference>("basecurrencyid").Id;
        var anchorId = env.Client.Create(new Entity("contact") { ["emailaddress1"] = E2eSpec.Types.AnchorEmail, ["lastname"] = "Anchor" });

        var plan = new E2eSpec.TypesPlan(decimalColumn, includeSeen, anchorId, currencyId);
        var jobId = CreateJob(env, ImportTemplate(env, E2eSpec.Types.Dto(plan)), E2eSpec.Types.Key, E2eSpec.Types.Csv(plan));
        var rows = E2eSpec.Types.Rows(plan);
        RunAndAssert(env, jobId, new JobExpect(ImportJobStatus.Completed, rows.Count, rows.Count));

        var columns = new List<string>
        {
            "lastname", "numberofchildren", "creditlimit", "transactioncurrencyid", "birthdate", "donotemail",
            "preferredcontactmethodcode", "gendercode", "parentcustomerid",
        };
        if (includeSeen) columns.Add("lastusedincampaign");
        if (decimalColumn is not null) columns.Add(decimalColumn);

        foreach (var row in rows)
        {
            var who = $"{row.Email} (fila {row.Index + 1})";
            var c = RequireContact(env, row.Email, columns.ToArray());

            Ensure(c.GetAttributeValue<string>("lastname") == E2eSpec.Types.DefaultLastName,
                $"{who}: lastname debía venir del DefaultValue '{E2eSpec.Types.DefaultLastName}', es '{c.GetAttributeValue<string>("lastname")}'");
            Ensure(c.GetAttributeValue<int?>("numberofchildren") == row.Children,
                $"{who}: Integer numberofchildren debía ser {row.Children}, es {c.GetAttributeValue<int?>("numberofchildren")}");
            Ensure(c.GetAttributeValue<Money>("creditlimit")?.Value == row.Credit,
                $"{who}: Money creditlimit debía ser {row.Credit.ToString(CultureInfo.InvariantCulture)}, es {c.GetAttributeValue<Money>("creditlimit")?.Value.ToString(CultureInfo.InvariantCulture) ?? "null"}");
            Ensure(c.GetAttributeValue<EntityReference>("transactioncurrencyid")?.Id == currencyId,
                $"{who}: Lookup por GUID transactioncurrencyid no quedó en la moneda base");
            Ensure(c.GetAttributeValue<DateTime?>("birthdate")?.Date == row.Birth.Date,
                $"{who}: DateTime birthdate debía ser {row.Birth:yyyy-MM-dd}, es {c.GetAttributeValue<DateTime?>("birthdate"):yyyy-MM-dd}");
            Ensure(c.GetAttributeValue<bool?>("donotemail") == row.Flag,
                $"{who}: Boolean donotemail con el token '{row.FlagToken}' debía ser {row.Flag}, es {c.GetAttributeValue<bool?>("donotemail")}");
            Ensure(c.GetAttributeValue<OptionSetValue>("preferredcontactmethodcode")?.Value == row.Pref,
                $"{who}: OptionSet (entero directo) preferredcontactmethodcode debía ser {row.Pref}, es {c.GetAttributeValue<OptionSetValue>("preferredcontactmethodcode")?.Value}");
            Ensure(c.GetAttributeValue<OptionSetValue>("gendercode")?.Value == row.Gender,
                $"{who}: OptionSet (OptionSetMapping) gendercode con '{row.GenderToken}' debía ser {row.Gender}, es {c.GetAttributeValue<OptionSetValue>("gendercode")?.Value}");

            if (includeSeen)
            {
                // Semántica: el CSV no trae zona horaria (Kind Unspecified); el SDK lo manda tal cual y Dataverse lo guarda como UTC.
                // El valor leído (UTC) debe ser EXACTAMENTE el literal del CSV, sin corrimiento por zona horaria.
                var stored = c.GetAttributeValue<DateTime?>("lastusedincampaign");
                Ensure(stored.HasValue && DateTime.SpecifyKind(stored.Value, DateTimeKind.Utc) == DateTime.SpecifyKind(row.Seen, DateTimeKind.Utc),
                    $"{who}: DateTime lastusedincampaign debía guardarse en UTC como {row.Seen:O}, es {stored:O}");
            }
            if (decimalColumn is not null)
                Ensure(c.GetAttributeValue<decimal?>(decimalColumn) == row.Dec,
                    $"{who}: Decimal {decimalColumn} debía ser {row.Dec.ToString(CultureInfo.InvariantCulture)}, es {c.GetAttributeValue<decimal?>(decimalColumn)?.ToString(CultureInfo.InvariantCulture) ?? "null"}");

            var parent = c.GetAttributeValue<EntityReference>("parentcustomerid");
            if (row.ExpectsParent)
                Ensure(parent is { LogicalName: "contact" } && parent.Id == anchorId,
                    $"{who}: Lookup parentcustomerid (valor '{(row.Index == 1 ? "GUID" : "emailaddress1 vía LookupMatchColumn")}') debía apuntar al anchor {anchorId}, apunta a {parent?.LogicalName}/{parent?.Id}");
            else
                Ensure(parent is null, $"{who}: parentcustomerid vacío en el CSV no debía resolverse a nada, apunta a {parent?.Id}");
        }
        Console.WriteLine($"[E2E-2]   {rows.Count} contacts verificados: String, Integer, Money, DateTime, Boolean (6 tokens), OptionSet (directo y con mapping), Lookup (GUID y business key)" +
                          (decimalColumn is not null ? ", Decimal." : "."));
    }

    // ─── [E2E-2g] Guid ───────────────────────────────────────────────────────────────────────────────────

    private static void GuidType(Env env)
    {
        var c = E2eSpec.GuidColumn.Case();
        var jobId = CreateJob(env, ImportTemplate(env, c.Dto), c.Key, c.Csv);
        RunAndAssert(env, jobId, new JobExpect(ImportJobStatus.Completed, E2eSpec.GuidColumn.Rows, E2eSpec.GuidColumn.Rows));

        for (var i = 1; i <= E2eSpec.GuidColumn.Rows; i++)
        {
            var contact = RequireContact(env, E2eSpec.GuidColumn.EmailOf(i));
            Ensure(contact.Id == E2eSpec.GuidColumn.IdFor(i),
                $"{E2eSpec.GuidColumn.EmailOf(i)}: Guid contactid debía ser {E2eSpec.GuidColumn.IdFor(i)}, es {contact.Id}");
        }
        Console.WriteLine("[E2E-2g]  Guid OK: los contacts nacieron con la clave primaria que traía el CSV.");
    }

    // ─── [E2E-3] IsRequired + DefaultValue ──────────────────────────────────────────────────────────────

    private static void RequiredAndDefault(Env env)
    {
        var jobId = CreateJob(env, ImportTemplate(env, E2eSpec.RequiredDefault.Dto()), E2eSpec.RequiredDefault.Key, E2eSpec.RequiredDefault.Csv());
        RunAndAssert(env, jobId, new JobExpect(ImportJobStatus.CompletedWithErrors, Total: 3, Success: 2, Failed: 1));

        var withDefault = RequireContact(env, E2eSpec.RequiredDefault.EmailOf(1), "firstname", "lastname");
        Ensure(withDefault.GetAttributeValue<string>("lastname") == E2eSpec.RequiredDefault.DefaultLastName,
            $"celda vacía + DefaultValue: lastname debía ser '{E2eSpec.RequiredDefault.DefaultLastName}', es '{withDefault.GetAttributeValue<string>("lastname")}'");
        var provided = RequireContact(env, E2eSpec.RequiredDefault.EmailOf(3), "lastname");
        Ensure(provided.GetAttributeValue<string>("lastname") == "Jones",
            $"celda con valor: el default NO debe pisarla, lastname debía ser 'Jones', es '{provided.GetAttributeValue<string>("lastname")}'");
        Ensure(ContactByEmail(env, E2eSpec.RequiredDefault.EmailOf(2)) is null,
            "la fila con un requerido vacío y sin default NO debe crear contact");

        var logs = LogsOf(env, jobId);
        Ensure(logs.Count == 1, $"debía haber 1 Import Job Log (la fila inválida), hay {logs.Count}");
        var log = logs[0];
        var message = log.GetAttributeValue<string>("sanic_smlt_ijl_errormessage") ?? "";
        Ensure(log.GetAttributeValue<int?>("sanic_smlt_ijl_rownumber") == E2eSpec.RequiredDefault.InvalidRowNumber,
            $"el log debía apuntar a la fila {E2eSpec.RequiredDefault.InvalidRowNumber}, apunta a {log.GetAttributeValue<int?>("sanic_smlt_ijl_rownumber")}");
        Ensure(log.GetAttributeValue<OptionSetValue>("sanic_smlt_ijl_errorcategory")?.Value == (int)ErrorCategory.ValidationError,
            $"errorcategory debía ser ValidationError({(int)ErrorCategory.ValidationError}), es {log.GetAttributeValue<OptionSetValue>("sanic_smlt_ijl_errorcategory")?.Value}");
        Ensure(message.Contains("firstname"), $"el mensaje debía nombrar la columna 'firstname': '{message}'");
        Ensure(!message.Contains(E2eSpec.Domain) && !message.Contains("Smith"), $"el mensaje no debe llevar datos de la fila (PII): '{message}'");
        Console.WriteLine($"[E2E-3]   default aplicado, requerido vacío rechazado: [{ErrorCategory.ValidationError}] {message}");
    }

    // ─── [E2E-8] dry run ─────────────────────────────────────────────────────────────────────────────────

    private static void DryRun(Env env)
    {
        var templateId = ImportTemplate(env, E2eSpec.Simple.Dto("dryrun", ImportTemplateStatus.Active));
        var csv = E2eSpec.Simple.Csv("dry", E2eSpec.Simple.Rows);
        var expect = new JobExpect(ImportJobStatus.Completed, E2eSpec.Simple.Rows, E2eSpec.Simple.Rows);

        var dryJob = CreateJob(env, templateId, "dryrun-simulado", csv, dryRun: true);
        RunAndAssert(env, dryJob, expect);
        Ensure(CountContactsWithPrefix(env, "dry") == 0,
            $"dry run: NO debía crear contacts y hay {CountContactsWithPrefix(env, "dry")}");

        // Contraste: el MISMO CSV sin dry run sí escribe (si no, "no crea nada" no probaría nada).
        var realJob = CreateJob(env, templateId, "dryrun-real", csv);
        RunAndAssert(env, realJob, expect);
        Ensure(CountContactsWithPrefix(env, "dry") == E2eSpec.Simple.Rows,
            $"el mismo CSV sin dry run debía crear {E2eSpec.Simple.Rows} contacts y hay {CountContactsWithPrefix(env, "dry")}");
        Console.WriteLine($"[E2E-8]   dry run: Completed con {E2eSpec.Simple.Rows} éxitos y 0 contacts; la corrida real del mismo CSV creó {E2eSpec.Simple.Rows}.");
    }

    // ─── [E2E-12] transforms ─────────────────────────────────────────────────────────────────────────────

    private static void Transforms(Env env)
    {
        var jobId = CreateJob(env, ImportTemplate(env, E2eSpec.Transforms.Dto()), E2eSpec.Transforms.Key, E2eSpec.Transforms.Csv());
        var expected = E2eSpec.Transforms.Expected;
        RunAndAssert(env, jobId, new JobExpect(ImportJobStatus.Completed, expected.Count, expected.Count));

        for (var i = 1; i <= expected.Count; i++)
        {
            var exp = expected[i - 1];
            var email = E2eSpec.Transforms.EmailOf(i);
            var c = RequireContact(env, email, "firstname", "lastname", "jobtitle", "birthdate", "department", "middlename");

            void Is(string column, string? want) =>
                Ensure(string.Equals(c.GetAttributeValue<string>(column), want, StringComparison.Ordinal),
                    $"{email}: {column} debía ser {(want is null ? "(vacío)" : $"'{want}'")}, es '{c.GetAttributeValue<string>(column)}'");

            Is("firstname", exp.First);            // UPPER
            Is("lastname", exp.Last);              // CONCAT
            Is("jobtitle", exp.JobTitle);          // MAP (sin match => se omite)
            Is("department", exp.Department);      // IF
            Is("middlename", exp.Middle);          // SPLIT
            Ensure(c.GetAttributeValue<DateTime?>("birthdate")?.Date == exp.Birth,
                $"{email}: birthdate (DATEPARSE dd/MM/yyyy) debía ser {exp.Birth:yyyy-MM-dd}, es {c.GetAttributeValue<DateTime?>("birthdate"):yyyy-MM-dd}");
        }
        Console.WriteLine("[E2E-12]  CONCAT, UPPER, MAP (con y sin match), DATEPARSE, IF y SPLIT evaluados por el plugin desplegado.");
    }
}
