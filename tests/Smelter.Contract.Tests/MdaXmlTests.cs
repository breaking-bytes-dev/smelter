using System.Xml.Linq;
using FluentAssertions;
using Smelter.Deploy;
using Xunit;

namespace Smelter.Contract.Tests;

/// <summary>
/// Parsers puros de <see cref="MdaXml"/> (formxml / fetchxml / layoutxml / sitemap) que usa
/// Smelter.Verify [MDA]. Se prueban con XML real de solutions/unpacked y con snippets mínimos
/// para los bordes (link-entity, aliases, urls de dashboard).
/// </summary>
public class MdaXmlTests
{
    private static string Unpacked(params string[] parts) =>
        Path.Combine(new[] { SolutionMetadata.RepoRoot, "solutions", "unpacked" }.Concat(parts).ToArray());

    private static string[] Tables => SecuritySpec.Tables.All.ToArray();

    [Fact]
    public void FormAttributes_ReadsDataFieldNamesFromARealMainForm()
    {
        var file = Directory.GetFiles(Unpacked("Entities", "sanic_tbl_smlt_importjob", "FormXml", "main"), "*.xml").Single();
        var formXml = XDocument.Load(file).Descendants("form").First().ToString();

        var attrs = MdaXml.FormAttributes(formXml);

        attrs.Should().Contain(new[] { "sanic_smlt_ij_name", "sanic_smlt_ij_status", "sanic_smlt_ij_sourcefile", "ownerid" });
        attrs.Should().NotContain("sanic_smlt_ij_isretry");
    }

    [Fact]
    public void FormAttributes_IncludesOnChangeEventAttributes()
    {
        const string xml = "<form><events><event name=\"onchange\" attribute=\"foo_bar\"><Handlers/></event></events>" +
                           "<tabs><tab><columns><column><sections><section><rows><row><cell>" +
                           "<control id=\"x\" datafieldname=\"foo_baz\"/></cell></row></rows></section></sections></column></columns></tab></tabs></form>";

        MdaXml.FormAttributes(xml).Should().BeEquivalentTo("foo_bar", "foo_baz");
    }

    [Fact]
    public void FormWebResources_ReadsLibraryNames_AndStripsWebresourcePrefix()
    {
        const string xml = "<form><formLibraries><Library name=\"sanic_a.js\" libraryUniqueId=\"{1}\"/>" +
                           "<Library name=\"$webresource:sanic_b.js\" libraryUniqueId=\"{2}\"/></formLibraries></form>";

        MdaXml.FormWebResources(xml).Should().BeEquivalentTo("sanic_a.js", "sanic_b.js");
    }

    [Fact]
    public void WebResourceRefs_ReadsRibbonJavaScriptLibraries()
    {
        var ribbon = File.ReadAllText(Unpacked("Entities", "sanic_tbl_smlt_importjob", "RibbonDiff.xml"));

        MdaXml.WebResourceRefs(ribbon).Should().Contain("sanic_smlt_importjob_actions.js");
    }

    [Fact]
    public void FetchAttributes_ReadsAttributesConditionsAndOrdersOfRootEntity()
    {
        var file = Directory.GetFiles(Unpacked("Entities", "sanic_tbl_smlt_importjob", "SavedQueries"), "{0225a5fb*.xml").Single();
        var fetch = XDocument.Load(file).Descendants("fetch").First().ToString();

        var (entity, attrs) = MdaXml.FetchAttributes(fetch);

        entity.Should().Be("sanic_tbl_smlt_importjob");
        attrs.Should().BeEquivalentTo("sanic_tbl_smlt_importjobid", "sanic_smlt_ij_name", "createdon", "statecode");
    }

    [Fact]
    public void FetchAttributes_IgnoresLinkEntitySubtreeAndAliasedConditions()
    {
        const string fetch =
            "<fetch><entity name=\"a\"><attribute name=\"a1\"/>" +
            "<order attribute=\"a2\"/>" +
            "<filter><condition attribute=\"a3\" operator=\"eq\" value=\"1\"/><condition entityname=\"lnk\" attribute=\"zz\" operator=\"eq\" value=\"1\"/></filter>" +
            "<link-entity name=\"b\" from=\"bid\" to=\"a1\" alias=\"lnk\"><attribute name=\"b1\"/></link-entity>" +
            "<all-attributes/></entity></fetch>";

        var (entity, attrs) = MdaXml.FetchAttributes(fetch);

        entity.Should().Be("a");
        attrs.Should().BeEquivalentTo("a1", "a2", "a3");
    }

    [Fact]
    public void LayoutAttributes_ReadsCellNames_SkippingAliasedOnes()
    {
        const string layout =
            "<grid name=\"x\" jump=\"n\"><row name=\"r\" id=\"rid\"><cell name=\"n\" width=\"1\"/><cell name=\"createdon\"/><cell name=\"lnk.other\"/></row></grid>";

        MdaXml.LayoutAttributes(layout).Should().BeEquivalentTo("n", "createdon");
    }

    [Fact]
    public void SiteMap_ParsesEntitySubAreasAndDashboardUrlOfTheRealApp()
    {
        var dir = Directory.GetDirectories(Unpacked("AppModuleSiteMaps")).Single();
        var xml = File.ReadAllText(Path.Combine(dir, "AppModuleSiteMap.xml")).TrimStart('﻿');

        var subAreas = MdaXml.SiteMapSubAreas(xml);

        subAreas.Where(s => s.Entity is not null).Select(s => s.Entity)
            .Should().BeEquivalentTo(
                "sanic_tbl_smlt_importtemplate", "sanic_tbl_smlt_importjob", "sanic_tbl_smlt_importjoblog");
        subAreas.Should().NotContain(s => s.Entity == HmacKeyTableSpec.Table);
        var dashboard = subAreas.Single(s => s.Url is not null);
        MdaXml.DashboardId(dashboard.Url!).Should().Be(Guid.Parse("590df35c-1b64-f111-ab0c-0022480a5be8"));
    }

    [Fact]
    public void MdaSpec_AppUniqueName_MatchesTheUnpackedAppModule()
    {
        var dir = Directory.GetDirectories(Unpacked("AppModules")).Single();
        Path.GetFileName(dir).Should().Be(MdaSpec.AppUniqueName);
        XDocument.Load(Path.Combine(dir, "AppModule.xml")).Root!.Element("UniqueName")!.Value.Should().Be(MdaSpec.AppUniqueName);
    }

    [Fact]
    public void DashboardId_IsNullForNonDashboardUrls()
    {
        MdaXml.DashboardId("/main.aspx?pagetype=entitylist&etn=contact").Should().BeNull();
        MdaXml.DashboardId("https://example.com/x").Should().BeNull();
    }

    [Fact]
    public void AppModule_ListsTheSixTablesAndNeverTheHmacKeyTable()
    {
        var dir = Directory.GetDirectories(Unpacked("AppModules")).Single();
        var doc = XDocument.Load(Path.Combine(dir, "AppModule.xml"));
        var entities = doc.Descendants("AppModuleComponent")
            .Where(c => (string?)c.Attribute("type") == "1")
            .Select(c => (string)c.Attribute("schemaName")!).ToList();

        entities.Should().BeEquivalentTo(Tables);
        entities.Should().NotContain(HmacKeyTableSpec.Table);
    }

    [Fact]
    public void RealForms_And_Views_NeverReferenceCustomAttributesMissingFromTheTable_NorTheSixDeletedOnes()
    {
        var deleted = SchemaCleanupCatalogo.Attributes.Select(a => a.AttributeLogicalName).ToHashSet();

        foreach (var table in Tables)
        {
            var folder = Unpacked("Entities", table);
            var known = XDocument.Load(Path.Combine(folder, "Entity.xml")).Descendants("attribute")
                .Select(a => (string?)a.Element("LogicalName")).Where(n => n is not null).Select(n => n!)
                .ToHashSet();

            var referenced = new HashSet<string>();
            foreach (var f in Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories))
            {
                var doc = XDocument.Load(f);
                foreach (var form in doc.Descendants("form")) referenced.UnionWith(MdaXml.FormAttributes(form.ToString()));
                foreach (var fx in doc.Descendants("fetch")) referenced.UnionWith(MdaXml.FetchAttributes(fx.ToString()).Attributes);
                foreach (var lx in doc.Descendants("grid")) referenced.UnionWith(MdaXml.LayoutAttributes(lx.ToString()));
            }

            referenced.Should().NotIntersectWith(deleted, $"{table}: forms/views no deben referenciar atributos eliminados en 1.3.0.0");
            var customUnknown = referenced.Where(a => a.StartsWith("sanic_") && !known.Contains(a) && a != table + "id");
            customUnknown.Should().BeEmpty($"{table}: forms/views referencian columnas custom inexistentes");
        }
    }
}
