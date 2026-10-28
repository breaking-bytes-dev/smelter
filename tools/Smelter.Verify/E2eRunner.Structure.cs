// Pasos [E2E-4a/4b], [E2E-6], [E2E-10], [E2E-14]: relaciones, estados del template y export/import. Ver E2eRunner.cs.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using static Smelter.Verify.ApiCalls;

namespace Smelter.Verify;

internal static partial class E2eRunner
{
    // ─── [E2E-4a / 4b] relación OneToMany ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Padre (contact "Parent") + hijo (contact "Child") del MISMO CSV, ProcessingOrder invertido a propósito (el DAG tiene que
    /// ganar) y FK <c>parentcustomerid</c> resuelto por la alternate key del padre. 4a: 150 filas en 2 chunks (100 + 50), las filas
    /// del 2º chunk referencian padres ya escritos en el 1º. 4b: padre keyed por HMAC (regresión del bug #8: la FK del hijo debe
    /// calcularse con el mismo transform del padre, no con el texto crudo del CSV).
    /// </summary>
    private static void Relationship(Env env, bool hmac)
    {
        var tag = hmac ? "4b" : "4a";
        string? keyHex = null;
        if (hmac) keyHex = HmacSmoke.CreateFreshKey(env.Client); // la key "smoke-verify" (nunca se imprime); Cleanup la borra

        var rows = hmac ? E2eSpec.Relationship.HmacRows : E2eSpec.Relationship.Rows;
        var dto = E2eSpec.Relationship.Dto(hmac);
        var jobId = CreateJob(env, ImportTemplate(env, dto), dto.Template.Name[E2eSpec.TemplatePrefix.Length..], E2eSpec.Relationship.Csv(hmac, rows));

        try
        {
            if (hmac)
            {
                RunJob(env, jobId, rows);
            }
            else
            {
                // Chunk a chunk, para comprobar los límites (100 + 50) y no solo el resultado final.
                var chunks = RunJob(env, jobId, rows);
                var expected = E2eSpec.ExpectedChunks(rows, E2eSpec.Relationship.ChunkSize);
                Ensure(chunks.Count == expected.Count, $"se esperaban {expected.Count} llamadas a ProcessChunk (100 + 50), hubo {chunks.Count}");
                for (var i = 0; i < expected.Count; i++)
                    Ensure(chunks[i].ProcessedSoFar == expected[i].ProcessedSoFar && chunks[i].HasMore == expected[i].HasMore,
                        $"chunk {i + 1}: ProcessedSoFar/HasMore debían ser {expected[i].ProcessedSoFar}/{expected[i].HasMore}, fueron {chunks[i].ProcessedSoFar}/{chunks[i].HasMore}");
            }
            // 2 table mappings => cada fila produce 2 resultados (padre e hijo): success cuenta (fila, tabla).
            AssertJob(env, jobId, new JobExpect(ImportJobStatus.Completed, rows, Success: rows * 2));
        }
        catch
        {
            DumpLogs(env, jobId);
            throw;
        }

        var contacts = AllE2eContacts(env, "emailaddress1", "parentcustomerid")
            .ToDictionary(c => c.GetAttributeValue<string>("emailaddress1"), c => c, StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i <= rows; i++)
        {
            var childEmail = E2eSpec.Relationship.ChildEmail(i, hmac);
            var parentEmail = E2eSpec.Relationship.ParentEmail(i, hmac, keyHex);
            Ensure(contacts.TryGetValue(childEmail, out var child), $"fila {i + 1}: no existe el hijo '{childEmail}'");
            Ensure(contacts.TryGetValue(parentEmail, out var parent),
                $"fila {i + 1}: no existe el padre esperado (email {(hmac ? "= HMAC(ParentKey) + dominio" : parentEmail)})");

            var fk = child!.GetAttributeValue<EntityReference>("parentcustomerid");
            Ensure(fk is not null, $"fila {i + 1}: el hijo '{childEmail}' quedó SIN padre (parentcustomerid vacío): la FK no se resolvió");
            Ensure(fk!.LogicalName == "contact" && fk.Id == parent!.Id,
                $"fila {i + 1}: el hijo '{childEmail}' apunta a {fk.LogicalName}/{fk.Id} y su padre es {parent!.Id}");
        }

        Console.WriteLine(hmac
            ? $"[E2E-{tag}]  {rows} hijos con la FK resuelta contra padres keyed por HMAC (la clave cruda del CSV no coincide con la del padre)."
            : $"[E2E-{tag}]  {rows} hijos con su FK resuelta ({E2eSpec.Relationship.DistinctParents} padres; chunks 100 + 50; ProcessingOrder invertido, el DAG ganó).");
    }

    // ─── [E2E-6] template Archived ───────────────────────────────────────────────────────────────────────

    private static void ArchivedRejected(Env env)
    {
        var dto = E2eSpec.Archived.Dto();
        var templateId = ImportTemplate(env, dto);
        var status = env.Client.Retrieve(TemplateTable, templateId, new ColumnSet("sanic_smlt_it_status"))
            .GetAttributeValue<OptionSetValue>("sanic_smlt_it_status")?.Value;
        Ensure(status == (int)ImportTemplateStatus.Archived,
            $"el import debía dejar el template Archived({(int)ImportTemplateStatus.Archived}) (status del JSON), quedó {status}");

        // Sin archivo: el gate de status corre ANTES de bajar el archivo.
        var jobId = CreateJob(env, templateId, "archived", csv: null);
        ExpectFault("PrepareImport con template Archived", () =>
            env.Client.Execute(new OrganizationRequest(env.Apis.Prepare) { ["JobId"] = jobId }), "not Active", "Archived");
        ExpectFault("ProcessChunk con template Archived", () =>
            env.Client.Execute(new OrganizationRequest(env.Apis.ProcessChunk) { ["JobId"] = jobId }), "not Active", "Archived");
        Console.WriteLine("[E2E-6]   Archived rechazado por PrepareImport y por ProcessChunk (gate de status).");
    }

    // ─── [E2E-10] roundtrip Export -> Import ──────────────────────────────────────────────────────────────

    private static void ExportImportRoundtrip(Env env)
    {
        var full = E2eSpec.Roundtrip.FullDto();
        var templateId = ImportTemplate(env, full);

        var json1 = Export(env, templateId);
        var export1 = new TemplateSerializer().Deserialize(json1);
        Ensure(export1.FormatVersion == TemplateSerializer.SupportedFormatVersion,
            $"el export debía declarar FormatVersion {TemplateSerializer.SupportedFormatVersion}, declara '{export1.FormatVersion}'");
        AssertNoDiff("lo exportado vs lo importado", E2eDtoComparer.Diff(full, export1));

        // Camino de UPDATE del import: re-importar el JSON exportado (mismo nombre) debe actualizar el mismo template y reconstruir
        // los mappings idénticos.
        var templateId2 = ImportTemplateJson(env, json1);
        Ensure(templateId2 == templateId, $"re-importar por nombre debía actualizar el template {templateId}, devolvió {templateId2}");
        var export2 = new TemplateSerializer().Deserialize(Export(env, templateId2));
        AssertNoDiff("export tras re-importar (update) vs export inicial", E2eDtoComparer.Diff(export1, export2));
        AssertNoDiff("export tras re-importar (update) vs DTO original", E2eDtoComparer.Diff(full, export2));

        var cms = full.TableMappings.Sum(t => t.ColumnMappings.Count);
        Console.WriteLine($"[E2E-10]  roundtrip sin diferencias: {full.TableMappings.Count} table mappings, {cms} column mappings (los 9 tipos), " +
                          $"{full.RelationshipMappings.Count} relación, description/status/insertonly incluidos; el update del import reconstruye lo mismo.");
    }

    private static string Export(Env env, Guid templateId) =>
        (string)env.Client.Execute(new OrganizationRequest(env.Apis.ExportTemplate) { ["TemplateId"] = templateId })["Json"];

    private static void AssertNoDiff(string what, IReadOnlyList<string> diffs) =>
        Ensure(diffs.Count == 0, $"roundtrip ({what}): {diffs.Count} diferencia(s):\n    " + string.Join("\n    ", diffs.Take(15)));

    // ─── [E2E-14] ManyToMany ───────────────────────────────────────────────────────────────────────────────

    private static void ManyToManyRejected(Env env)
    {
        ExpectFault("el import de un template con relación ManyToMany(1)", () => ImportTemplateJson(env, E2eSpec.ManyToMany.Json()),
            E2eSpec.ManyToMany.ExpectedFault);

        var created = RetrieveAll(env.Client, new QueryExpression(TemplateTable)
        {
            ColumnSet = new ColumnSet(false),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("sanic_smlt_it_name",
                        ConditionOperator.Equal, E2eSpec.TemplateName("manytomany")),
                },
            },
        });
        Ensure(created.Count == 0, "el import rechazado no debe dejar un template a medias, pero existe uno con ese nombre");
        Console.WriteLine("[E2E-14]  relación ManyToMany rechazada por la Custom API de import, sin crear nada.");
    }
}
