using System.Reflection;
using System.Text;
using FluentAssertions;
using Smelter.Deploy;
using Smelter.Plugins.Models;
using Smelter.Plugins.Services;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// Piezas PURAS de los pasos [E2E-n] de Smelter.Verify (E2eSteps / E2eSpec*): selección con --only, nombres y dominio de
/// limpieza, CSV determinísticos, DTO de cada caso validados con las mismas reglas que PrepareImport, y los valores
/// esperados de los transforms contrastados contra el TransformEngine REAL.
/// </summary>
public class E2eSpecTests
{
    // ─── --only ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Catalog_HasUniqueIdsAndTitles_AndCoversEveryCaseOfTheAudit()
    {
        E2eSteps.All.Select(s => s.Id).Should().OnlyHaveUniqueItems();
        E2eSteps.All.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Title));
        E2eSteps.All.Select(s => s.Id).Should().Contain(
            ["1", "2", "2g", "3", "4a", "4b", "5a", "5b", "5c", "6", "7a", "7b", "8", "10", "11", "12", "13", "14"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("e2e")]
    [InlineData("ALL")]
    public void Select_WithoutFilterOrWithGroupAll_ReturnsEverything(string? only) =>
        E2eSteps.Select(only).Should().Equal(E2eSteps.All);

    [Fact]
    public void Select_ExactId_ReturnsOnlyThatStep() =>
        E2eSteps.Select("4b").Select(s => s.Id).Should().Equal("4b");

    [Fact]
    public void Select_NumberSelectsTheWholeGroup_ButNotOtherNumbers() =>
        E2eSteps.Select("4").Select(s => s.Id).Should().Equal("4a", "4b");

    [Fact]
    public void Select_NumberDoesNotMatchLongerNumbersThatShareThePrefix() =>
        E2eSteps.Select("1").Select(s => s.Id).Should().Equal("1"); // no "10", "11", "12", "13", "14"

    [Fact]
    public void Select_AcceptsPrefixAndBracketsAndCaseAndKeepsCatalogOrderWithoutDuplicates() =>
        E2eSteps.Select("[E2E-5c], 1 ,e2e-1").Select(s => s.Id).Should().Equal("1", "5c");

    [Fact]
    public void Select_UnknownStep_FailsNamingItAndListingTheValidOnes()
    {
        var act = () => E2eSteps.Select("1,zz");

        var ex = act.Should().Throw<ArgumentException>().Which;
        ex.Message.Should().Contain("zz").And.Contain("4a").And.Contain("--only");
    }

    [Fact]
    public void OnlyArgument_ParsesBothFormsAndAbsence()
    {
        E2eSteps.OnlyArgument(["--bench"]).Should().BeNull();
        E2eSteps.OnlyArgument(["--interactive", "--only", "2,3"]).Should().Be("2,3");
        E2eSteps.OnlyArgument(["--only=7a"]).Should().Be("7a");
    }

    [Fact]
    public void OnlyArgument_WithoutValue_Fails()
    {
        var act = () => E2eSteps.OnlyArgument(["--only"]);
        act.Should().Throw<ArgumentException>().WithMessage("*--only*");
    }

    // ─── nombres y dominio de limpieza ────────────────────────────────────────────────────────

    [Fact]
    public void Domain_IsDistinctFromEveryOtherSmokeDomain_SoCleanupsNeverOverlap()
    {
        var others = new[] { "@smelter-smoke.test", MultiChunkSmokeSpec.Domain, HmacSmoke_Domain };
        foreach (var other in others)
        {
            E2eSpec.Domain.EndsWith(other, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
            other.EndsWith(E2eSpec.Domain, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        }
    }

    private const string HmacSmoke_Domain = "@smelter-hmac-smoke.test"; // HmacSmoke.Domain vive en Verify (no referenciable desde acá)

    [Fact]
    public void Names_AreOwnedByTheE2ePrefixes_AndNeverCollideWithTheOtherSmokes()
    {
        E2eSpec.AllTemplateNames.Should().OnlyHaveUniqueItems();
        E2eSpec.AllTemplateNames.Should().OnlyContain(n => n.StartsWith(E2eSpec.TemplatePrefix));
        E2eSpec.AllTemplateNames.Should().NotContain(["Smelter Smoke Test", "Smelter HMAC Smoke Test", MultiChunkSmokeSpec.TemplateName, BenchSpec.TemplateName]);
        E2eSpec.JobName("types").Should().StartWith(E2eSpec.JobPrefix);
        E2eSpec.Email("a").Should().Be("a" + E2eSpec.Domain);
        new[] { "Smoke Test — Smelter.Verify", "Smoke Test HMAC — Smelter.Verify", MultiChunkSmokeSpec.JobName, BenchSpec.JobName }
            .Should().OnlyContain(n => !n.StartsWith(E2eSpec.JobPrefix));
    }

    [Fact]
    public void EveryTemplateTheSpecBuilds_IsDeclaredInAllTemplateNames()
    {
        var built = AllCases().Select(c => c.Dto.Template.Name).Distinct().ToList();
        E2eSpec.AllTemplateNames.Should().Contain(built);
    }

    // ─── CSV ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData("\t")]
    public void Csv_QuotesFieldsThatContainDelimiterQuoteOrNewline_AndRoundTripsThroughTheRealParser(string delimiter)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Email", "Notes" },
            new[] { "a@x", $"has{delimiter}delimiter" },
            new[] { "b@x", "has \"quote\"" },
            new[] { "c@x", "line1\nline2" },
            new[] { "d@x", "plain" },
        };

        var csv = E2eSpec.Csv(delimiter, rows);
        csv.Should().NotEndWith("\n");

        var parsed = new CsvParserService().ReadRange(new UTF8Encoding(false).GetBytes(csv), true, delimiter, 0, int.MaxValue).ToList();
        parsed.Select(r => r.Values["Notes"].Replace("\r\n", "\n")).Should().Equal(
            $"has{delimiter}delimiter", "has \"quote\"", "line1\nline2", "plain");
    }

    [Fact]
    public void ExpectedChunks_FollowsProcessChunkSemantics_PartialLastChunkEndsTheJob()
    {
        E2eSpec.ExpectedChunks(150, 100).Should().Equal(new ExpectedChunk(100, 100, true), new ExpectedChunk(50, 150, false));
        E2eSpec.ExpectedChunks(250, 100).Select(c => c.Rows).Should().Equal(100, 100, 50);
    }

    // ─── cada template + CSV pasa las reglas de Prepare ────────────────────────────────────────────

    public static IEnumerable<object[]> PrepareableCases() => AllCases().Where(c => c.ShouldPassPrepare)
        .Select(c => new object[] { c.Label, c });

    [Theory]
    [MemberData(nameof(PrepareableCases))]
    public void EveryRunnableTemplate_PassesThePrepareRules_AndTheCsvHasTheDeclaredRows(string label, CaseUnderTest c)
    {
        _ = label;
        E2eSpecTestKit.PrepareProblems(c.Dto, c.Csv).Should().BeEmpty();
        E2eSpecTestKit.CountRows(c.Dto, c.Csv).Should().Be(c.Rows);
        TemplateRoundTrips(c.Dto);
    }

    [Fact]
    public void EveryRunnableTemplate_UsesAnActiveStatusAndFormat11()
    {
        foreach (var c in AllCases().Where(c => c.ShouldPassPrepare))
        {
            c.Dto.FormatVersion.Should().Be("1.1", c.Label);
            c.Dto.Template.Status.Should().Be((int)ImportTemplateStatus.Active, c.Label);
            c.Dto.Template.Name.Should().StartWith(E2eSpec.TemplatePrefix, c.Label);
        }
    }

    [Fact]
    public void EveryContactWrittenByTheSpec_HasEmailsUnderTheE2eDomain_SoTheCleanupFindsThem()
    {
        foreach (var c in AllCases().Where(c => c.ShouldPassPrepare))
        {
            var rows = E2eSpecTestKit.ReadRows(c.Dto, c.Csv);
            foreach (var tm in c.Dto.TableMappings.Where(t => t.TargetTable == "contact"))
            {
                var email = tm.ColumnMappings.Single(m => m.TargetColumn == "emailaddress1");
                foreach (var row in rows)
                {
                    var key = email.SourceColumn ?? "";
                    if (string.IsNullOrEmpty(email.TransformExpression) && key.Length > 0)
                        row.Values[key].Should().EndWith(E2eSpec.Domain, c.Label);
                }
            }
        }
    }

    // ─── [E2E-1] CreateIfNotExists=false ──────────────────────────────────────────────────────────

    [Fact]
    public void CreateIfNotExists_Template_CreatesNothingButUpdates_AndTheCsvMixesExistingAndMissing()
    {
        var c = E2eSpec.CreateIfNotExists.Case();
        var tm = c.Dto.TableMappings.Single();
        tm.CreateIfNotExists.Should().BeFalse();
        tm.UpdateIfExists.Should().BeTrue();
        tm.InsertOnly.Should().BeFalse();

        E2eSpec.CreateIfNotExists.ExistingEmails.Should().HaveCount(2);
        c.Rows.Select(r => r.Email).Should().Equal(E2eSpec.CreateIfNotExists.ExistingEmails.Append(E2eSpec.CreateIfNotExists.MissingEmail));
        c.Rows.Should().OnlyContain(r => r.First != E2eSpec.CreateIfNotExists.OldFirstName);
    }

    // ─── [E2E-2] tipos de dato ──────────────────────────────────────────────────────────────────────

    private static E2eSpec.TypesPlan Plan(string? dec = "some_decimal", bool seen = true) =>
        new(dec, seen, new Guid("aaaaaaaa-0000-0000-0000-000000000001"), new Guid("bbbbbbbb-0000-0000-0000-000000000002"));

    [Fact]
    public void Types_MapsEveryColumnDataType_WhenTheEnvironmentOffersEveryOptionalColumn()
    {
        var dto = E2eSpec.Types.Dto(Plan());

        var types = dto.TableMappings.Single().ColumnMappings.Select(c => (ColumnDataType)c.DataType).Distinct().ToList();
        // Guid va en su propio paso (2g, aislado por riesgo): el resto de los 9 tipos debe estar acá.
        types.Should().BeEquivalentTo(Enum.GetValues<ColumnDataType>().Where(t => t != ColumnDataType.Guid));
    }

    [Fact]
    public void Types_OptionalColumns_AreOmittedWhenTheEnvironmentLacksThem()
    {
        var targets = E2eSpec.Types.Dto(Plan(dec: null, seen: false)).TableMappings.Single().ColumnMappings.Select(c => c.TargetColumn).ToList();

        targets.Should().NotContain("some_decimal").And.NotContain("lastusedincampaign");
        E2eSpec.Types.Dto(Plan()).TableMappings.Single().ColumnMappings.Select(c => c.TargetColumn)
            .Should().Contain(["some_decimal", "lastusedincampaign"]);
    }

    [Fact]
    public void Types_Dto_PassesPrepare_WithAndWithoutOptionalColumns()
    {
        foreach (var plan in new[] { Plan(), Plan(null, false) })
        {
            var dto = E2eSpec.Types.Dto(plan);
            E2eSpecTestKit.PrepareProblems(dto, E2eSpec.Types.Csv(plan)).Should().BeEmpty();
            E2eSpecTestKit.CountRows(dto, E2eSpec.Types.Csv(plan)).Should().Be(E2eSpec.Types.Rows(plan).Count);
        }
    }

    [Fact]
    public void Types_Rows_CoverEveryBooleanTokenTheEngineAccepts_AndBothOptionSetPaths()
    {
        var rows = E2eSpec.Types.Rows(Plan());

        rows.Select(r => r.FlagToken.ToLowerInvariant()).Should().BeEquivalentTo("true", "false", "1", "0", "yes", "no");
        rows.Where(r => r.Flag).Select(r => r.FlagToken.ToLowerInvariant()).Should().BeEquivalentTo("true", "1", "yes");
        rows.Select(r => r.Flag).Distinct().Should().BeEquivalentTo(new[] { true, false });

        var mapping = E2eSpec.Types.Dto(Plan()).TableMappings.Single().ColumnMappings.Single(c => c.TargetColumn == "gendercode");
        mapping.OptionSetMapping.Should().Be("{\"M\":1,\"F\":2}");
        rows.Select(r => r.GenderToken).Distinct().Should().BeEquivalentTo("M", "F");
        rows.Where(r => r.GenderToken == "M").Should().OnlyContain(r => r.Gender == 1);
        rows.Where(r => r.GenderToken == "F").Should().OnlyContain(r => r.Gender == 2);

        var direct = E2eSpec.Types.Dto(Plan()).TableMappings.Single().ColumnMappings.Single(c => c.TargetColumn == "preferredcontactmethodcode");
        direct.OptionSetMapping.Should().BeNullOrEmpty(); // entero directo, sin mapping
        rows.Select(r => r.Pref).Should().OnlyContain(p => p >= 1 && p <= 5);
    }

    [Fact]
    public void Types_Lookups_UseGuidAndBusinessKey_AndTheOthersAreEmpty()
    {
        var plan = Plan();
        var rows = E2eSpec.Types.Rows(plan);
        var lookup = E2eSpec.Types.Dto(plan).TableMappings.Single().ColumnMappings.Single(c => c.TargetColumn == "parentcustomerid");

        lookup.DataType.Should().Be((int)ColumnDataType.Lookup);
        lookup.LookupTable.Should().Be("contact");
        lookup.LookupMatchColumn.Should().Be("emailaddress1");

        rows[0].ParentToken.Should().Be(plan.AnchorId.ToString());
        rows[1].ParentToken.Should().Be(E2eSpec.Types.AnchorEmail);
        rows.Skip(2).Should().OnlyContain(r => r.ParentToken == "");
        rows.Count(r => r.ExpectsParent).Should().Be(2);

        var currency = E2eSpec.Types.Dto(plan).TableMappings.Single().ColumnMappings.Single(c => c.TargetColumn == "transactioncurrencyid");
        currency.LookupTable.Should().Be("transactioncurrency");
        rows.Should().OnlyContain(r => r.CurrencyToken == plan.CurrencyId.ToString());
    }

    [Fact]
    public void Types_Values_AreLiteralsTheCoercionRulesAccept()
    {
        var rows = E2eSpec.Types.Rows(Plan());
        var csv = E2eSpecTestKit.ReadRows(E2eSpec.Types.Dto(Plan()), E2eSpec.Types.Csv(Plan()));

        for (var i = 0; i < rows.Count; i++)
        {
            var v = csv[i].Values;
            int.Parse(v["Children"], System.Globalization.CultureInfo.InvariantCulture).Should().Be(rows[i].Children);
            decimal.Parse(v["Credit"], System.Globalization.CultureInfo.InvariantCulture).Should().Be(rows[i].Credit);
            decimal.Parse(v["Dec"], System.Globalization.CultureInfo.InvariantCulture).Should().Be(rows[i].Dec);
            DateTime.Parse(v["Birth"], System.Globalization.CultureInfo.InvariantCulture).Date.Should().Be(rows[i].Birth.Date);
            DateTime.Parse(v["Seen"], System.Globalization.CultureInfo.InvariantCulture).Should().Be(rows[i].Seen);
            v["Email"].Should().Be(rows[i].Email);
        }
        rows.Select(r => r.Credit).Should().OnlyContain(c => decimal.Round(c, 2) == c); // la moneda del Dev redondea a 2
    }

    // ─── [E2E-2g] Guid aislado ────────────────────────────────────────────────────────────────────

    [Fact]
    public void GuidColumn_TargetsThePrimaryKey_WithDeterministicDistinctIds()
    {
        var c = E2eSpec.GuidColumn.Case();
        var guid = c.Dto.TableMappings.Single().ColumnMappings.Single(m => m.DataType == (int)ColumnDataType.Guid);

        guid.TargetColumn.Should().Be("contactid");
        E2eSpec.GuidColumn.IdFor(1).Should().NotBe(E2eSpec.GuidColumn.IdFor(2));
        E2eSpec.GuidColumn.IdFor(1).Should().Be(E2eSpec.GuidColumn.IdFor(1));
        E2eSpecTestKit.ReadRows(c.Dto, c.Csv).Select(r => Guid.Parse(r.Values["Id"])).Should().Equal(
            Enumerable.Range(1, E2eSpec.GuidColumn.Rows).Select(E2eSpec.GuidColumn.IdFor));
    }

    // ─── [E2E-3] Required + Default ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RequiredDefault_OneRowFallsBackToTheDefault_AndOneViolatesTheRequiredWithoutDefault()
    {
        var dto = E2eSpec.RequiredDefault.Dto();
        var cms = dto.TableMappings.Single().ColumnMappings;
        cms.Single(c => c.TargetColumn == "lastname").IsRequired.Should().BeTrue();
        cms.Single(c => c.TargetColumn == "lastname").DefaultValue.Should().Be(E2eSpec.RequiredDefault.DefaultLastName);
        cms.Single(c => c.TargetColumn == "firstname").IsRequired.Should().BeTrue();
        cms.Single(c => c.TargetColumn == "firstname").DefaultValue.Should().BeNullOrEmpty();

        var rows = E2eSpecTestKit.ReadRows(dto, E2eSpec.RequiredDefault.Csv());
        rows.Should().HaveCount(3);
        rows[E2eSpec.RequiredDefault.DefaultRow - 1].Values["Last"].Should().BeEmpty();
        rows[E2eSpec.RequiredDefault.DefaultRow - 1].Values["First"].Should().NotBeEmpty();
        rows[E2eSpec.RequiredDefault.InvalidRow - 1].Values["First"].Should().BeEmpty();
        rows[E2eSpec.RequiredDefault.InvalidRow - 1].RowNumber.Should().Be(E2eSpec.RequiredDefault.InvalidRowNumber);
        E2eSpec.RequiredDefault.InvalidRowNumber.Should().Be(3); // cabecera = 1
    }

    // ─── [E2E-4] relaciones ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Relationship_ProcessingOrderIsDeliberatelyInverted_ButTheDagPutsTheParentFirst(bool hmac)
    {
        var dto = E2eSpec.Relationship.Dto(hmac);
        var parent = dto.TableMappings.Single(t => t.Name.Contains("Parent"));
        var child = dto.TableMappings.Single(t => t.Name.Contains("Child"));
        child.ProcessingOrder.Should().BeLessThan(parent.ProcessingOrder, "el orden numérico dice hijo primero: el DAG debe ganar");

        var ctx = E2eSpecTestKit.ToContext(dto);
        ctx.TableMappings.First().Name.Should().Be(child.Name); // así lo carga el loader (ORDER BY processingorder)
        DependencyResolver.Resolve(ctx.TableMappings, ctx.RelationshipMappings).Select(t => t.Name)
            .Should().Equal(parent.Name, child.Name);
    }

    [Fact]
    public void Relationship_ParentsAreReusedByTheSecondChunk_SoChildrenReferenceParentsWrittenEarlier()
    {
        E2eSpec.Relationship.Rows.Should().Be(150);
        E2eSpec.Relationship.ChunkSize.Should().Be(100);
        E2eSpec.ExpectedChunks(E2eSpec.Relationship.Rows, E2eSpec.Relationship.ChunkSize).Should().HaveCount(2);

        var chunk1 = Enumerable.Range(1, 100).Select(E2eSpec.Relationship.ParentIndex).ToHashSet();
        var chunk2 = Enumerable.Range(101, 50).Select(E2eSpec.Relationship.ParentIndex).ToList();
        chunk2.Should().OnlyContain(p => chunk1.Contains(p));
        E2eSpec.Relationship.DistinctParents.Should().Be(chunk1.Count);
        Enumerable.Range(1, 150).Select(E2eSpec.Relationship.ChildEmail).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Relationship_Plain_ChildAndParentKeysComeFromTheCsvColumnsTheMappingNames()
    {
        var dto = E2eSpec.Relationship.Dto(hmac: false);
        var rel = dto.RelationshipMappings.Single();
        rel.RelationshipType.Should().Be((int)RelationshipType.OneToMany);
        rel.LookupColumnOnChild.Should().Be("parentcustomerid");
        rel.ParentDedupColumns.Should().Be("ParentKey");

        var rows = E2eSpecTestKit.ReadRows(dto, E2eSpec.Relationship.Csv(hmac: false, E2eSpec.Relationship.Rows));
        rows.Should().HaveCount(150);
        rows[0].Values["ParentKey"].Should().Be(E2eSpec.Relationship.ParentEmail(1, hmac: false, null));
        rows[149].Values["Email"].Should().Be(E2eSpec.Relationship.ChildEmail(150));
        rows[149].Values["ParentKey"].Should().Be(E2eSpec.Relationship.ParentEmail(150, hmac: false, null));
        rows[149].Values["ParentKey"].Should().Be(rows[49].Values["ParentKey"], "la fila 150 usa el padre de la fila 50 (chunk 1)");
    }

    [Fact]
    public void Relationship_Hmac_ParentKeyIsDerivedByTheTransform_AndTheExpectationMatchesTheRealEngine()
    {
        var dto = E2eSpec.Relationship.Dto(hmac: true);
        var parent = dto.TableMappings.Single(t => t.Name.Contains("Parent"));
        var emailMapping = parent.ColumnMappings.Single(c => c.TargetColumn == "emailaddress1");
        emailMapping.TransformExpression.Should().NotBeNullOrEmpty();

        var keyHex = HmacSmokeSpec.NewKeyHex();
        var engine = new TransformEngine(new FixedKeyProvider(keyHex));
        var rows = E2eSpecTestKit.ReadRows(dto, E2eSpec.Relationship.Csv(hmac: true, E2eSpec.Relationship.HmacRows));

        rows.Should().HaveCount(E2eSpec.Relationship.HmacRows);
        foreach (var (row, i) in rows.Select((r, i) => (r, i + 1)))
        {
            engine.Evaluate(emailMapping.TransformExpression, row.Values)
                .Should().Be(E2eSpec.Relationship.ParentEmail(i, hmac: true, keyHex));
            row.Values["ParentKey"].Should().NotEndWith(E2eSpec.Domain, "el CSV trae la clave cruda; el email sale del HMAC");
        }
        E2eSpec.Relationship.ParentEmail(1, true, keyHex).Should().EndWith(E2eSpec.Domain).And.HaveLength(64 + E2eSpec.Domain.Length);
    }

    private sealed class FixedKeyProvider(string keyHex) : IHmacKeyProvider
    {
        public byte[] GetKey(string keyName) => keyName == HmacSmokeSpec.KeyLabel ? Convert.FromHexString(keyHex) : null!;
    }

    // ─── [E2E-5] formatos de CSV ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Formats_CoverHeaderlessSemicolonAndTab_WithDelimiterLookalikesInTheData()
    {
        var all = E2eSpec.Formats.All;

        all.Select(f => f.Key).Should().Equal("noheader", "semicolon", "tab");
        all[0].Dto.Template.HasHeaderRow.Should().BeFalse();
        all[0].Dto.TableMappings.Single().ColumnMappings.Select(c => c.SourceColumn)
            .Should().Contain(["Column0", "Column1", "Column2"]);
        all[1].Dto.Template.Delimiter.Should().Be(";");
        all[1].Rows.Select(r => r.First).Should().Contain(r => r.Contains(','), "una coma dentro de un CSV de ';' no debe separar");
        all[2].Dto.Template.Delimiter.Should().Be("TAB", "el alias que entiende CsvDelimiter, tipeable en el form");
        all[2].Rows.Select(r => r.First).Should().Contain(r => r.Contains(';'));
        all[2].Csv.Should().Contain("\t");

        foreach (var f in all)
        {
            var rows = E2eSpecTestKit.ReadRows(f.Dto, f.Csv);
            rows.Should().HaveCount(f.Rows.Count, f.Key);
        }
    }

    [Fact]
    public void Formats_HeaderlessFile_HasNoHeaderLine()
    {
        var f = E2eSpec.Formats.All[0];
        f.Csv.Split('\n')[0].Should().StartWith(f.Rows[0].Email);
    }

    // ─── [E2E-11] case-insensitive ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void CaseInsensitive_HeaderAndMappingDifferOnlyInCase()
    {
        var c = E2eSpec.CaseInsensitive.Case();
        var mapped = c.Dto.TableMappings.Single().ColumnMappings.Select(m => m.SourceColumn!).ToList();
        var headers = new CsvParserService().ReadHeaders(new UTF8Encoding(false).GetBytes(c.Csv), true, ",");

        mapped.Should().NotBeEmpty();
        foreach (var m in mapped)
        {
            headers.Should().NotContain(m, "ordinal");
            headers.Should().Contain(h => string.Equals(h, m, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ─── [E2E-12] transforms ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Transforms_ExpectedValues_MatchTheRealTransformEngine()
    {
        var dto = E2eSpec.Transforms.Dto();
        var engine = new TransformEngine();
        var rows = E2eSpecTestKit.ReadRows(dto, E2eSpec.Transforms.Csv());

        rows.Should().HaveCount(E2eSpec.Transforms.Expected.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var exp = E2eSpec.Transforms.Expected[i];
            string Eval(string target) => engine.Evaluate(
                dto.TableMappings.Single().ColumnMappings.Single(c => c.TargetColumn == target).TransformExpression, rows[i].Values);

            Eval("firstname").Should().Be(exp.First);                       // UPPER
            Eval("lastname").Should().Be(exp.Last);                         // CONCAT
            Eval("jobtitle").Should().Be(exp.JobTitle ?? "");              // MAP (sin match => vacío => se omite)
            Eval("department").Should().Be(exp.Department);                 // IF
            Eval("middlename").Should().Be(exp.Middle);                     // SPLIT
            DateTime.Parse(Eval("birthdate"), System.Globalization.CultureInfo.InvariantCulture).Date
                .Should().Be(exp.Birth);                                    // DATEPARSE
        }
    }

    [Fact]
    public void Transforms_UseEveryFunctionTheAuditListed()
    {
        var expressions = string.Join(" ", E2eSpec.Transforms.Dto().TableMappings.Single().ColumnMappings
            .Select(c => c.TransformExpression));

        foreach (var fn in new[] { "CONCAT(", "MAP(", "DATEPARSE(", "IF(", "SPLIT(", "UPPER(" })
            expressions.Should().Contain(fn);
        E2eSpec.Transforms.Expected.Select(e => e.JobTitle).Should().Contain((string?)null, "una fila sin match en MAP");
    }

    // ─── [E2E-6/14] estados y relaciones no soportadas ──────────────────────────────────────────────

    [Fact]
    public void Archived_TemplateTravelsAsArchivedAndIsOtherwiseValid()
    {
        var dto = E2eSpec.Archived.Dto();
        dto.Template.Status.Should().Be((int)ImportTemplateStatus.Archived);
        E2eSpecTestKit.PrepareProblems(dto, E2eSpec.Simple.Csv("arch", 2)).Should().BeEmpty();
        TemplateRoundTrips(dto);
    }

    [Fact]
    public void ManyToMany_JsonIsRejectedByTheSerializerWithTheMessageVerifyAsserts()
    {
        var json = E2eSpec.ManyToMany.Json();

        var act = () => new TemplateSerializer().Deserialize(json);
        act.Should().Throw<TemplateFormatException>().WithMessage($"*{E2eSpec.ManyToMany.ExpectedFault}*");
        E2eSpec.ManyToMany.ExpectedFault.Should().Be("ManyToMany");
        json.Should().Contain("\"RelationshipType\":1");
    }

    // ─── [E2E-7/8] simple (flow y dry run) ───────────────────────────────────────────────────────────

    [Fact]
    public void Simple_Draft_IsDraft_AndItsRowsNeverAppearInAnyMessageToken()
    {
        E2eSpec.Simple.Dto("flow-draft", ImportTemplateStatus.Draft).Template.Status.Should().Be((int)ImportTemplateStatus.Draft);
        E2eSpec.Simple.PiiTokens.Should().Contain(E2eSpec.Domain);
        E2eSpec.Simple.Csv("flowdraft", 3).Should().Contain(E2eSpec.Simple.PiiTokens[0]);
    }

    // ─── [E2E-10] roundtrip ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Roundtrip_FullDto_CarriesEveryDtoFieldWithANonDefaultValueSomewhere_SoNewFieldsCannotHideFromTheRoundtrip()
    {
        var dto = E2eSpec.Roundtrip.FullDto();

        AssertNonDefaultSomewhere(new[] { dto.Template }, skip: ["SourceType", "HasHeaderRow"]); // SourceType 0 = CSV es el único válido; HasHeaderRow=false ES el valor no-default del schema (default true)
        AssertNonDefaultSomewhere(dto.TableMappings, skip: ["Ref"]);
        AssertNonDefaultSomewhere(dto.TableMappings.SelectMany(t => t.ColumnMappings), skip: []);
        AssertNonDefaultSomewhere(dto.RelationshipMappings, skip: ["RelationshipType"]); // 0 = OneToMany es el único valor válido

        dto.TableMappings.SelectMany(t => t.ColumnMappings).Select(c => c.DataType).Distinct()
            .Should().BeEquivalentTo(Enum.GetValues<ColumnDataType>().Select(t => (int)t));
        dto.TableMappings.Should().Contain(t => t.InsertOnly).And.Contain(t => !t.CreateIfNotExists).And.Contain(t => !t.UpdateIfExists);
        dto.TableMappings.Should().Contain(t => t.CreateIfNotExists).And.Contain(t => t.UpdateIfExists);
        dto.Template.Status.Should().Be((int)ImportTemplateStatus.Archived);
        dto.Template.HasHeaderRow.Should().BeFalse();
        dto.Template.Description.Should().NotBeNullOrWhiteSpace();
        TemplateRoundTrips(dto);
    }

    private static void AssertNonDefaultSomewhere<T>(IEnumerable<T> items, string[] skip)
    {
        var list = items.ToList();
        foreach (var p in typeof(T).GetProperties().Where(p => !skip.Contains(p.Name)))
        {
            var defaultValue = p.PropertyType.IsValueType ? Activator.CreateInstance(p.PropertyType) : null;
            list.Any(i => !Equals(p.GetValue(i), defaultValue) && !(p.GetValue(i) is string s && s.Length == 0) && !(p.GetValue(i) is System.Collections.ICollection { Count: 0 }))
                .Should().BeTrue($"{typeof(T).Name}.{p.Name} debe tener un valor no-default en el DTO del roundtrip");
        }
    }

    [Fact]
    public void Comparer_IgnoresRefsOrderAndNullVsEmpty_ButDetectsEveryFieldChange()
    {
        var expected = E2eSpec.Roundtrip.FullDto();
        var actual = Clone(expected);
        // refs distintos y orden invertido (el exporter ordena por ProcessingOrder y regenera los slugs)
        foreach (var tm in actual.TableMappings) tm.Ref = "other-" + tm.Ref;
        foreach (var rel in actual.RelationshipMappings) { rel.ParentRef = "other-" + rel.ParentRef; rel.ChildRef = "other-" + rel.ChildRef; }
        actual.TableMappings.Reverse();
        foreach (var tm in actual.TableMappings) tm.ColumnMappings.Reverse();
        foreach (var cm in actual.TableMappings.SelectMany(t => t.ColumnMappings).Where(c => c.DefaultValue == null)) cm.DefaultValue = "";

        E2eDtoComparer.Diff(expected, actual).Should().BeEmpty();
    }

    public static IEnumerable<object[]> MutableFields()
    {
        foreach (var p in typeof(TemplateInfoDto).GetProperties()) yield return ["Template", typeof(TemplateInfoDto), p.Name];
        foreach (var p in typeof(TableMappingDto).GetProperties().Where(p => p.Name != "Ref")) yield return ["TableMapping", typeof(TableMappingDto), p.Name];
        foreach (var p in typeof(ColumnMappingDto).GetProperties()) yield return ["ColumnMapping", typeof(ColumnMappingDto), p.Name];
        foreach (var p in typeof(RelationshipMappingDto).GetProperties()) yield return ["Relationship", typeof(RelationshipMappingDto), p.Name];
    }

    [Theory]
    [MemberData(nameof(MutableFields))]
    public void Comparer_DetectsAChangeInEveryDtoProperty(string level, Type type, string property)
    {
        var expected = E2eSpec.Roundtrip.FullDto();
        var actual = Clone(expected);
        object target = level switch
        {
            "Template" => actual.Template,
            "TableMapping" => actual.TableMappings.First(t => t.ColumnMappings.Count > 0),
            "ColumnMapping" => actual.TableMappings.First(t => t.ColumnMappings.Count > 0).ColumnMappings.First(),
            _ => actual.RelationshipMappings.First(),
        };
        var prop = type.GetProperty(property)!;
        var current = prop.GetValue(target);

        if (property is "ParentRef") prop.SetValue(target, actual.TableMappings.Select(t => t.Ref).First(r => r != (string)current!));
        else if (property is "ChildRef") prop.SetValue(target, actual.TableMappings.Select(t => t.Ref).First(r => r != (string)current!));
        else if (prop.PropertyType == typeof(string)) prop.SetValue(target, (string?)current + "-changed");
        else if (prop.PropertyType == typeof(bool)) prop.SetValue(target, !(bool)current!);
        else if (prop.PropertyType == typeof(int)) prop.SetValue(target, (int)current! + 1);
        else if (prop.PropertyType == typeof(int?)) prop.SetValue(target, ((int?)current ?? 0) + 1);
        else if (prop.PropertyType == typeof(List<ColumnMappingDto>)) ((List<ColumnMappingDto>)current!).RemoveAt(0);
        else throw new InvalidOperationException($"Tipo no soportado por el test: {prop.PropertyType} ({property})");

        E2eDtoComparer.Diff(expected, actual).Should().NotBeEmpty($"un cambio en {level}.{property} debe verse en el roundtrip");
    }

    [Fact]
    public void Comparer_ReportsWhatAndWhere()
    {
        var expected = E2eSpec.Roundtrip.FullDto();
        var actual = Clone(expected);
        actual.TableMappings[0].ColumnMappings[0].TargetColumn += "x";
        actual.Template.Description = "otra";

        var diff = E2eDtoComparer.Diff(expected, actual);

        diff.Should().Contain(d => d.Contains("Description") && d.Contains("otra"));
        diff.Should().Contain(d => d.Contains("TargetColumn") && d.Contains(expected.TableMappings[0].Name));
    }

    // ─── helpers ──────────────────────────────────────────────────────────────────────────────────────────

    public sealed record CaseUnderTest(string Label, TemplateExportDto Dto, string Csv, int Rows, bool ShouldPassPrepare);

    private static List<CaseUnderTest> AllCases()
    {
        var list = new List<CaseUnderTest>();

        void Add(string label, TemplateExportDto dto, string csv, int rows, bool runnable = true) =>
            list.Add(new CaseUnderTest(label, dto, csv, rows, runnable));

        var cine = E2eSpec.CreateIfNotExists.Case();
        Add("1 createifnotexists", cine.Dto, cine.Csv, cine.Rows.Count);

        var plan = Plan();
        Add("2 types", E2eSpec.Types.Dto(plan), E2eSpec.Types.Csv(plan), E2eSpec.Types.Rows(plan).Count);

        var g = E2eSpec.GuidColumn.Case();
        Add("2g guid", g.Dto, g.Csv, g.Rows.Count);

        Add("3 required", E2eSpec.RequiredDefault.Dto(), E2eSpec.RequiredDefault.Csv(), 3);

        Add("4a relationship", E2eSpec.Relationship.Dto(false), E2eSpec.Relationship.Csv(false, E2eSpec.Relationship.Rows), E2eSpec.Relationship.Rows);
        Add("4b relationship hmac", E2eSpec.Relationship.Dto(true), E2eSpec.Relationship.Csv(true, E2eSpec.Relationship.HmacRows), E2eSpec.Relationship.HmacRows);

        foreach (var f in E2eSpec.Formats.All) Add("5 " + f.Key, f.Dto, f.Csv, f.Rows.Count);

        Add("6 archived", E2eSpec.Archived.Dto(), E2eSpec.Simple.Csv("arch", 2), 2, runnable: false);

        var simple = E2eSpec.Simple.Dto("dryrun", ImportTemplateStatus.Active);
        Add("8 dryrun", simple, E2eSpec.Simple.Csv("dry", E2eSpec.Simple.Rows), E2eSpec.Simple.Rows);
        Add("7a flow", E2eSpec.Simple.Dto("flow", ImportTemplateStatus.Active), E2eSpec.Simple.Csv("flow", E2eSpec.Simple.Rows), E2eSpec.Simple.Rows);
        Add("7b flow draft", E2eSpec.Simple.Dto("flow-draft", ImportTemplateStatus.Draft), E2eSpec.Simple.Csv("flowdraft", 3), 3, runnable: false);

        var ci = E2eSpec.CaseInsensitive.Case();
        Add("11 caseinsensitive", ci.Dto, ci.Csv, ci.Rows.Count);

        Add("12 transforms", E2eSpec.Transforms.Dto(), E2eSpec.Transforms.Csv(), E2eSpec.Transforms.Expected.Count);

        Add("10 roundtrip", E2eSpec.Roundtrip.FullDto(), "", 0, runnable: false);
        return list;
    }

    private static void TemplateRoundTrips(TemplateExportDto dto)
    {
        var serializer = new TemplateSerializer();
        var back = serializer.Deserialize(serializer.Serialize(dto));
        E2eDtoComparer.Diff(dto, back).Should().BeEmpty();
    }

    private static TemplateExportDto Clone(TemplateExportDto dto)
    {
        var serializer = new TemplateSerializer();
        return serializer.Deserialize(serializer.Serialize(dto));
    }
}
