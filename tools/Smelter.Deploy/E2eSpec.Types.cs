// [E2E-2] Tipos de dato (un solo template "types" sobre contact), [E2E-2g] Guid aislado, [E2E-4] relaciones y [E2E-12] transforms.
// Ver E2eSpec.cs para el contexto. Las columnas de contact elegidas (todas estándar):
//   String firstname/lastname · Integer numberofchildren · Money creditlimit (+ transactioncurrencyid por GUID: la moneda base de la org)
//   DateTime birthdate (DateOnly) y lastusedincampaign (DateTime; solo si el Dev lo permite) · Boolean donotemail (los 6 tokens)
//   OptionSet preferredcontactmethodcode (entero directo) y gendercode (con OptionSetMapping)
//   Decimal: contact NO trae una columna Decimal escribible estándar; el runner busca una en la metadata (plan.DecimalColumn) y,
//   si no hay, el paso lo informa como SKIP en vez de inventar una.
//   Lookup parentcustomerid -> contact (por GUID y por emailaddress1 con LookupMatchColumn).

using System.Globalization;
using Smelter.Plugins.Models;

namespace Smelter.Deploy;

internal static partial class E2eSpec
{
    /// <summary>Lo que el runner descubre en el Dev (metadata) y el anchor/moneda que crea o lee antes del import.</summary>
    internal sealed record TypesPlan(string? DecimalColumn, bool IncludeSeen, Guid AnchorId, Guid CurrencyId);

    internal sealed record TypesRow(int Index, string Email, string First, int Children, decimal Credit, DateTime Birth, DateTime Seen,
        string FlagToken, bool Flag, int Pref, string GenderToken, int Gender, decimal Dec, string ParentToken, bool ExpectsParent,
        string CurrencyToken);

    internal static class Types
    {
        internal const string Key = "types";
        internal const string DefaultLastName = "Types";
        internal static readonly string AnchorEmail = Email("types-anchor");

        private static readonly string[] FlagTokens = ["True", "false", "1", "0", "YES", "no"];
        private static readonly bool[] Flags = [true, false, true, false, true, false];

        internal static IReadOnlyList<TypesRow> Rows(TypesPlan plan) =>
            Enumerable.Range(1, FlagTokens.Length).Select(i => new TypesRow(
                Index: i,
                Email: Email($"types{i}"),
                First: $"Typed{i}",
                Children: i,
                Credit: i * 100 + 0.25m,
                Birth: new DateTime(1980 + i, 3, 15),
                Seen: new DateTime(2024, 5, i, 13, 45, 0),
                FlagToken: FlagTokens[i - 1],
                Flag: Flags[i - 1],
                Pref: ((i - 1) % 5) + 1,
                GenderToken: i % 2 == 1 ? "M" : "F",
                Gender: i % 2 == 1 ? 1 : 2,
                Dec: i + 0.5m,
                ParentToken: i == 1 ? plan.AnchorId.ToString() : i == 2 ? AnchorEmail : "",
                ExpectsParent: i <= 2,
                CurrencyToken: plan.CurrencyId.ToString())).ToList();

        internal static string Csv(TypesPlan plan) => E2eSpec.Csv(",",
            new[] { Row("Email", "First", "Children", "Credit", "Birth", "Seen", "Flag", "Pref", "Gender", "Dec", "Parent", "Currency") }
                .Concat(Rows(plan).Select(r => Row(
                    r.Email, r.First, r.Children.ToString(CultureInfo.InvariantCulture),
                    r.Credit.ToString("F2", CultureInfo.InvariantCulture), r.Birth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    r.Seen.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), r.FlagToken,
                    r.Pref.ToString(CultureInfo.InvariantCulture), r.GenderToken, r.Dec.ToString("F1", CultureInfo.InvariantCulture),
                    r.ParentToken, r.CurrencyToken))));

        internal static TemplateExportDto Dto(TypesPlan plan)
        {
            var cols = new List<ColumnMappingDto>
            {
                Col("emailaddress1", "Email"),
                Col("firstname", "First"),
                Col("lastname", null, defaultValue: DefaultLastName),
                Col("numberofchildren", "Children", ColumnDataType.Integer),
                Col("creditlimit", "Credit", ColumnDataType.Money),
                Col("transactioncurrencyid", "Currency", ColumnDataType.Lookup, lookupTable: "transactioncurrency"),
                Col("birthdate", "Birth", ColumnDataType.DateTime),
                Col("donotemail", "Flag", ColumnDataType.Boolean),
                Col("preferredcontactmethodcode", "Pref", ColumnDataType.OptionSet),
                Col("gendercode", "Gender", ColumnDataType.OptionSet, optionSetMapping: "{\"M\":1,\"F\":2}"),
                Col("parentcustomerid", "Parent", ColumnDataType.Lookup, lookupTable: "contact", lookupMatch: "emailaddress1"),
            };
            if (plan.IncludeSeen) cols.Add(Col("lastusedincampaign", "Seen", ColumnDataType.DateTime));
            if (plan.DecimalColumn is { Length: > 0 } dec) cols.Add(Col(dec, "Dec", ColumnDataType.Decimal));

            return Template(Key, true, ",", 0, ImportTemplateStatus.Active,
            [
                ContactMapping("tm-types", "Contact (tipos de dato)", 1, "emailaddress1", true, true, cols.ToArray()),
            ]);
        }
    }

    // ─── [E2E-2g] Guid ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aislado del template "types": un contact con la clave primaria (contactid) dada por el CSV. Si Dataverse rechaza
    /// un Upsert con KeyAttributes + id explícito, solo este paso falla y los otros 8 tipos siguen demostrados.
    /// </summary>
    internal static class GuidColumn
    {
        internal const string Key = "types-guid";
        internal const int Rows = 3;

        internal static Guid IdFor(int i) => new($"0000000{i}-e2e0-4000-8000-00000000e2e{i}");
        internal static string EmailOf(int i) => Email($"guid{i}");

        internal static ContactCase Case()
        {
            var dto = Template(Key, true, ",", 0, ImportTemplateStatus.Active,
            [
                ContactMapping("tm-guid", "Contact (Guid)", 1, "emailaddress1", true, true,
                    Col("emailaddress1", "Email"), Col("contactid", "Id", ColumnDataType.Guid),
                    Col("lastname", null, defaultValue: "Guid")),
            ]);
            var rows = Enumerable.Range(1, Rows).Select(i => new ContactRow(EmailOf(i), "", "")).ToList();
            var csv = E2eSpec.Csv(",", new[] { Row("Email", "Id") }.Concat(
                Enumerable.Range(1, Rows).Select(i => Row(EmailOf(i), IdFor(i).ToString()))));
            return new ContactCase(Key, dto, csv, rows);
        }
    }

    // ─── [E2E-4] relación OneToMany ─────────────────────────────────────────────────────────────────────────────

    internal static class Relationship
    {
        internal const int Rows = 150;
        internal const int ChunkSize = 100;
        internal const int HmacRows = 4;
        internal const int DistinctParents = 100;

        /// <summary>Las filas 101..150 reutilizan los padres 1..50, ya escritos en el chunk 1.</summary>
        internal static int ParentIndex(int row) => ((row - 1) % DistinctParents) + 1;

        private static string Prefix(bool hmac) => hmac ? "relh" : "rel";

        internal static string ChildEmail(int row) => ChildEmail(row, false);
        internal static string ChildEmail(int row, bool hmac) => Email($"{Prefix(hmac)}-child{row:D3}");

        /// <summary>Valor del CSV (columna ParentKey): el email del padre en el caso plano; una clave cruda (tipo PAN) en el HMAC.</summary>
        internal static string RawParentKey(int row, bool hmac) =>
            hmac ? $"relh-raw-{ParentIndex(row):D3}" : Email($"rel-parent{ParentIndex(row):D3}");

        /// <summary>Email con el que queda el padre de esa fila en Dataverse (en HMAC: hash(key) + dominio).</summary>
        internal static string ParentEmail(int row, bool hmac, string? keyHex) =>
            hmac ? HmacSmokeSpec.ComputeHex(keyHex ?? throw new ArgumentNullException(nameof(keyHex)), RawParentKey(row, true)) + Domain
                 : RawParentKey(row, false);

        internal static string Csv(bool hmac, int rows) => E2eSpec.Csv(",",
            new[] { Row("Email", "ParentKey") }
                .Concat(Enumerable.Range(1, rows).Select(i => Row(ChildEmail(i, hmac), RawParentKey(i, hmac)))));

        internal static TemplateExportDto Dto(bool hmac)
        {
            // ProcessingOrder INVERTIDO a propósito: el hijo dice 1 y el padre 2. Con la relación, el DAG tiene que ganar.
            var child = ContactMapping("tm-rel-child", "Child (contact)", 1, "emailaddress1", true, true,
                Col("emailaddress1", "Email"), Col("lastname", null, defaultValue: "Child"));

            var parentEmail = hmac
                ? Col("emailaddress1", null, transform: $"CONCAT({HmacSmokeSpec.TransformExpression("ParentKey")}, \"{Domain}\")")
                : Col("emailaddress1", "ParentKey");
            var parent = ContactMapping("tm-rel-parent", "Parent (contact)", 2, "emailaddress1", true, true,
                parentEmail, Col("lastname", null, defaultValue: "Parent"));

            return Template(hmac ? "relationship-hmac" : "relationship", true, ",", ChunkSize, ImportTemplateStatus.Active,
                [child, parent],
                [
                    new RelationshipMappingDto
                    {
                        Name = "Parent -> Child", ParentRef = parent.Ref, ChildRef = child.Ref,
                        RelationshipType = (int)RelationshipType.OneToMany, LookupColumnOnChild = "parentcustomerid",
                        ParentDedupColumns = "ParentKey",
                    },
                ]);
        }
    }

    // ─── [E2E-12] transforms ────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record TransformExpectation(string First, string Last, string? JobTitle, string Department, string Middle, DateTime Birth);

    internal static class Transforms
    {
        internal const string Key = "transforms";

        private static readonly string[] Firsts = ["Ada", "Grace", "Alan"];
        private static readonly string[] Lasts = ["Lovelace", "Hopper", "Turing"];
        private static readonly string[] Codes = ["A", "B", "C"];
        private static readonly string[] Dates = ["15/03/1990", "01/12/1985", "31/07/2000"];

        internal static string EmailOf(int i) => Email($"transform{i}");

        internal static readonly IReadOnlyList<TransformExpectation> Expected =
        [
            new("ADA", "Ada-Lovelace", "Alpha", "is-a", "q1", new DateTime(1990, 3, 15)),
            new("GRACE", "Grace-Hopper", "Beta", "not-a", "q2", new DateTime(1985, 12, 1)),
            new("ALAN", "Alan-Turing", null, "not-a", "q3", new DateTime(2000, 7, 31)), // código "C": MAP sin match => vacío => se omite
        ];

        internal static string Csv() => E2eSpec.Csv(",",
            new[] { Row("Email", "First", "Last", "Code", "Dt", "Full") }.Concat(
                Enumerable.Range(1, Expected.Count).Select(i =>
                    Row(EmailOf(i), Firsts[i - 1], Lasts[i - 1], Codes[i - 1], Dates[i - 1], $"p{i}|q{i}|r{i}"))));

        internal static TemplateExportDto Dto() => Template(Key, true, ",", 0, ImportTemplateStatus.Active,
        [
            ContactMapping("tm-transforms", "Contact (transforms)", 1, "emailaddress1", true, true,
                Col("emailaddress1", "Email"),
                Col("firstname", null, transform: "UPPER(First)"),
                Col("lastname", null, transform: "CONCAT(First, \"-\", Last)"),
                Col("jobtitle", null, transform: """MAP(Code, "{""A"":""Alpha"",""B"":""Beta""}")"""),
                Col("birthdate", null, ColumnDataType.DateTime, transform: "DATEPARSE(Dt, \"dd/MM/yyyy\")"),
                Col("department", null, transform: "IF(Code, \"A\", \"is-a\", \"not-a\")"),
                Col("middlename", null, transform: "SPLIT(Full, \"|\", 1)")),
        ]);
    }
}
