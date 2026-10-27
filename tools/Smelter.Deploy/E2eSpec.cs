// Piezas PURAS de los pasos [E2E-n] de Smelter.Verify (E2eRunner*): nombres, dominio de limpieza, builders de CSV y de los
// TemplateExportDto de cada caso, con sus valores ESPERADOS. Nada acá toca Dataverse: los tests de contrato validan cada
// template con las reglas de PrepareImport (TemplateValidator, gate de dedup, headers) y los transforms con el TransformEngine
// real, así un error de armado se descubre en un test y no en una corrida contra Dev.
//
// Todo dato de contact que escribe un E2E termina en Domain: la limpieza borra por ese sufijo (hijos antes que padres).
// Todo template E2E empieza con TemplatePrefix y todo job con JobPrefix: nunca se pisa con el smoke principal ni con CHUNK/HMAC/bench.
//
// Tablas objetivo: SOLO contact. Su alternate key (emailaddress1) es la única garantizada en Dev; `account` no tiene una
// conocida, y una relación padre/hijo exige que el padre tenga alternate key. Por eso las relaciones son contact -> contact por
// `parentcustomerid` (lookup Customer: acepta account o contact).

using System.Text;
using Smelter.Plugins.Models;

namespace Smelter.Deploy;

internal sealed record ContactRow(string Email, string First, string Last);

internal sealed record ContactCase(string Key, TemplateExportDto Dto, string Csv, IReadOnlyList<ContactRow> Rows);

internal static partial class E2eSpec
{
    internal const string Domain = "@smelter-e2e.test";
    internal const string TemplatePrefix = "Smelter E2E ";
    internal const string JobPrefix = "E2E — ";

    /// <summary>Claves de los templates que importan los pasos (nombre = TemplatePrefix + clave).</summary>
    internal static readonly string[] TemplateKeys =
    [
        "createifnotexists", "types", "types-guid", "required", "relationship", "relationship-hmac",
        "format-noheader", "format-semicolon", "format-tab", "caseinsensitive", "transforms",
        "dryrun", "flow", "flow-draft", "archived", "manytomany", "roundtrip",
    ];

    internal static IReadOnlyList<string> AllTemplateNames => TemplateKeys.Select(TemplateName).ToList();

    internal static string Email(string local) => local + Domain;
    internal static string TemplateName(string key) => TemplatePrefix + key;
    internal static string JobName(string key) => JobPrefix + key;

    /// <summary>CSV con "\n", sin newline final (el cursor de bytes debe terminar en el largo exacto). Cita lo que lo necesita.</summary>
    internal static string Csv(string delimiter, IEnumerable<IReadOnlyList<string>> rows) =>
        string.Join("\n", rows.Select(r => string.Join(delimiter, r.Select(f => Quote(f, delimiter)))));

    private static string Quote(string field, string delimiter) =>
        field.Contains(delimiter) || field.Contains('"') || field.Contains('\n') || field.Contains('\r')
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;

    /// <summary>Lo que devuelve cada llamada a ProcessChunk (el último chunk parcial termina el job).</summary>
    internal static IReadOnlyList<ExpectedChunk> ExpectedChunks(int rows, int chunkSize)
    {
        var list = new List<ExpectedChunk>();
        for (var done = 0; done < rows;)
        {
            var n = Math.Min(chunkSize, rows - done);
            done += n;
            list.Add(new ExpectedChunk(n, done, n >= chunkSize));
        }
        return list;
    }

    // ─── builders compartidos ──────────────────────────────────────────────────────────────────────

    internal static TemplateExportDto Template(string key, bool header, string delimiter, int chunkSize, ImportTemplateStatus status,
        IEnumerable<TableMappingDto> tableMappings, IEnumerable<RelationshipMappingDto>? relationships = null,
        string? description = null) => new()
    {
        FormatVersion = "1.1",
        Template = new TemplateInfoDto
        {
            Name = TemplateName(key), SourceType = 0, HasHeaderRow = header, Delimiter = delimiter,
            ChunkSize = chunkSize, Description = description, Status = (int)status,
        },
        TableMappings = tableMappings.ToList(),
        RelationshipMappings = (relationships ?? Array.Empty<RelationshipMappingDto>()).ToList(),
    };

    internal static TableMappingDto ContactMapping(string reference, string name, int order, string? dedup,
        bool create, bool update, params ColumnMappingDto[] columns) => new()
    {
        Ref = reference, Name = name, TargetTable = "contact", ProcessingOrder = order, DedupColumns = dedup,
        CreateIfNotExists = create, UpdateIfExists = update, ColumnMappings = columns.ToList(),
    };

    internal static ColumnMappingDto Col(string target, string? source, ColumnDataType type = ColumnDataType.String,
        string? transform = null, bool required = false, string? defaultValue = null, string? lookupTable = null,
        string? lookupMatch = null, string? optionSetMapping = null) => new()
    {
        Name = target, SourceColumn = source, TargetColumn = target, DataType = (int)type, IsRequired = required,
        DefaultValue = defaultValue, LookupTable = lookupTable, LookupMatchColumn = lookupMatch,
        OptionSetMapping = optionSetMapping, TransformExpression = transform,
    };

    private static IReadOnlyList<string> Row(params string[] fields) => fields;

    // ─── [E2E-1] CreateIfNotExists = false ────────────────────────────────────────────────────────────

    internal static class CreateIfNotExists
    {
        internal const string Key = "createifnotexists";
        internal const string OldFirstName = "Old";

        /// <summary>Se crean ANTES del import (por SDK): son los que el template puede actualizar.</summary>
        internal static readonly IReadOnlyList<string> ExistingEmails = [Email("cine-exist1"), Email("cine-exist2")];
        internal static readonly string MissingEmail = Email("cine-missing");

        internal static ContactCase Case()
        {
            var dto = Template(Key, true, ",", 0, ImportTemplateStatus.Active,
            [
                ContactMapping("tm-cine", "Contact (create-if-not-exists = false)", 1, "emailaddress1", create: false, update: true,
                    Col("emailaddress1", "Email"), Col("firstname", "First"), Col("lastname", "Last")),
            ]);
            var rows = ExistingEmails.Concat(new[] { MissingEmail })
                .Select((e, i) => new ContactRow(e, $"New{i + 1}", "Cine")).ToList();
            var csv = Csv(",", new[] { Row("Email", "First", "Last") }.Concat(rows.Select(r => Row(r.Email, r.First, r.Last))));
            return new ContactCase(Key, dto, csv, rows);
        }
    }

    // ─── [E2E-3] IsRequired + DefaultValue ───────────────────────────────────────────────────────────────

    internal static class RequiredDefault
    {
        internal const string Key = "required";
        internal const string DefaultLastName = "Fallback";

        /// <summary>Fila (1-based, de datos) con Last vacío: el default la salva.</summary>
        internal const int DefaultRow = 1;
        /// <summary>Fila (1-based, de datos) con First vacío y sin default: ValidationError.</summary>
        internal const int InvalidRow = 2;
        /// <summary>RowNumber físico de la fila inválida en el log (cabecera = 1).</summary>
        internal const int InvalidRowNumber = InvalidRow + 1;

        internal static string EmailOf(int dataRow) => Email($"req{dataRow}");

        internal static TemplateExportDto Dto() => Template(Key, true, ",", 0, ImportTemplateStatus.Active,
        [
            ContactMapping("tm-req", "Contact (required + default)", 1, "emailaddress1", true, true,
                Col("emailaddress1", "Email"),
                Col("firstname", "First", required: true),
                Col("lastname", "Last", required: true, defaultValue: DefaultLastName)),
        ]);

        internal static string Csv() => E2eSpec.Csv(",",
        [
            Row("Email", "First", "Last"),
            Row(EmailOf(1), "Ada", ""),
            Row(EmailOf(2), "", "Smith"),
            Row(EmailOf(3), "Bob", "Jones"),
        ]);
    }

    // ─── [E2E-5] formatos de CSV ───────────────────────────────────────────────────────────────────────────

    internal static class Formats
    {
        private static ContactCase Build(string key, bool header, string delimiterSetting, string delimiterChar, string[] sources, string prefix)
        {
            var rows = new List<ContactRow>
            {
                new(Email(prefix + "1"), delimiterChar == ";" ? "Ann,Marie" : delimiterChar == "\t" ? "Ann;Marie" : "Ann", "Zed"),
                new(Email(prefix + "2"), "Bea", "Yin"),
                new(Email(prefix + "3"), "Cy", "Xu"),
            };
            var data = rows.Select(r => Row(r.Email, r.First, r.Last));
            var lines = header ? new[] { Row("Email", "First", "Last") }.Concat(data) : data;
            var dto = Template("format-" + key, header, delimiterSetting, 0, ImportTemplateStatus.Active,
            [
                ContactMapping("tm-fmt", $"Contact (formato {key})", 1, "emailaddress1", true, true,
                    Col("emailaddress1", sources[0]), Col("firstname", sources[1]), Col("lastname", sources[2])),
            ]);
            return new ContactCase(key, dto, E2eSpec.Csv(delimiterChar, lines), rows);
        }

        internal static readonly IReadOnlyList<ContactCase> All =
        [
            Build("noheader", false, ",", ",", ["Column0", "Column1", "Column2"], "fmt-a"),
            Build("semicolon", true, ";", ";", ["Email", "First", "Last"], "fmt-b"),
            Build("tab", true, "TAB", "\t", ["Email", "First", "Last"], "fmt-c"),
        ];

        /// <summary>Ids de paso -> caso (5a/5b/5c).</summary>
        internal static ContactCase ForStep(string stepId) => stepId switch
        {
            "5a" => All[0],
            "5b" => All[1],
            "5c" => All[2],
            _ => throw new ArgumentException($"Paso de formato desconocido: {stepId}"),
        };
    }

    // ─── [E2E-11] SourceColumn sin distinguir mayúsculas ───────────────────────────────────────────────────

    internal static class CaseInsensitive
    {
        internal const string Key = "caseinsensitive";

        internal static ContactCase Case()
        {
            // El mapping dice "Email"/"FirstName"/"LastName"; el archivo trae todo en minúsculas.
            var dto = Template(Key, true, ",", 0, ImportTemplateStatus.Active,
            [
                ContactMapping("tm-ci", "Contact (case-insensitive)", 1, "emailaddress1", true, true,
                    Col("emailaddress1", "Email"), Col("firstname", "FirstName"), Col("lastname", "LastName")),
            ]);
            var rows = new List<ContactRow> { new(Email("ci1"), "Lower", "Case"), new(Email("ci2"), "Mixed", "Header") };
            var csv = E2eSpec.Csv(",", new[] { Row("email", "firstname", "lastname") }.Concat(rows.Select(r => Row(r.Email, r.First, r.Last))));
            return new ContactCase(Key, dto, csv, rows);
        }
    }

    // ─── [E2E-8 / E2E-7] template simple (dry run y Cloud Flow) ────────────────────────────────────────────────

    internal static class Simple
    {
        internal const int Rows = 5;

        /// <summary>Lo que un motivo de falla PII-free NO puede contener (dominio y prefijo de los emails del CSV del Draft).</summary>
        internal static readonly IReadOnlyList<string> PiiTokens = [Domain, "flowdraft"];

        internal static TemplateExportDto Dto(string key, ImportTemplateStatus status) =>
            Template(key, true, ",", 0, status,
            [
                ContactMapping("tm-simple", "Contact (simple)", 1, "emailaddress1", true, true,
                    Col("emailaddress1", "Email"), Col("firstname", "FirstName"), Col("lastname", "LastName")),
            ]);

        internal static string EmailOf(string prefix, int i) => Email($"{prefix}{i}");

        internal static string Csv(string prefix, int rows) => E2eSpec.Csv(",",
            new[] { Row("Email", "FirstName", "LastName") }
                .Concat(Enumerable.Range(1, rows).Select(i => Row(EmailOf(prefix, i), $"Simple{i}", "Verify"))));
    }

    // ─── [E2E-6] Archived ──────────────────────────────────────────────────────────────────────────────────────

    internal static class Archived
    {
        internal static TemplateExportDto Dto() => Simple.Dto("archived", ImportTemplateStatus.Archived);
    }

    // ─── [E2E-14] ManyToMany ───────────────────────────────────────────────────────────────────────────────────

    internal static class ManyToMany
    {
        /// <summary>Fragmento del mensaje de TemplateSerializer que el import debe devolver.</summary>
        internal const string ExpectedFault = "ManyToMany";

        internal static string Json()
        {
            var dto = Template("manytomany", true, ",", 0, ImportTemplateStatus.Active,
            [
                ContactMapping("tm-a", "Contact A", 1, "emailaddress1", true, true, Col("emailaddress1", "Email")),
                ContactMapping("tm-b", "Contact B", 2, "emailaddress1", true, true, Col("emailaddress1", "Email")),
            ],
            [
                new RelationshipMappingDto
                {
                    Name = "N:M (no soportada)", ParentRef = "tm-a", ChildRef = "tm-b", RelationshipType = 1,
                    LookupColumnOnChild = "parentcustomerid", ParentDedupColumns = "Email",
                },
            ]);
            return new Smelter.Plugins.Services.TemplateSerializer().Serialize(dto);
        }
    }
}
