// Lectura de main forms con sus etiquetas en LOS DOS idiomas. Dataverse devuelve el formxml solo con las etiquetas del
// idioma de interfaz de quien lo lee (verificado en Dev: el mismo form leído en 1033 trae solo label 1033 y en 3082 solo
// label 3082, aunque ambos estén guardados). Sin esto, FormPolish nunca "ve" el 3082: el deploy lo reescribe en cada
// corrida y verify lo da por faltante.
//
// RetrieveMainForms lee en el idioma actual, cambia TEMPORALMENTE usersettings.uilanguageid del usuario conectado a 3082,
// relee y lo restaura (finally). MergeSpanishLabels (pura) copia los label 3082 al documento 1033 por id de elemento.

using System.Xml.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Smelter.Deploy;

internal static class FormLabelsReader
{
    private static readonly string SpanishCode = Lcids.Spanish.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Main forms (type 2) de la entidad con el formxml fusionado 1033 + 3082. Opcionalmente solo los activos.</summary>
    internal static IReadOnlyList<Entity> RetrieveMainForms(ServiceClient client, string entity, bool onlyActive = false)
    {
        var english = Query(client, entity, onlyActive);

        var userId = ((Microsoft.Crm.Sdk.Messages.WhoAmIResponse)client.Execute(new Microsoft.Crm.Sdk.Messages.WhoAmIRequest())).UserId;
        var settings = client.Retrieve("usersettings", userId, new ColumnSet("uilanguageid"));
        var original = settings.GetAttributeValue<int>("uilanguageid");

        IReadOnlyList<Entity> spanish;
        try
        {
            client.Update(new Entity("usersettings", userId) { ["uilanguageid"] = Lcids.Spanish });
            spanish = Query(client, entity, onlyActive);
        }
        finally
        {
            client.Update(new Entity("usersettings", userId) { ["uilanguageid"] = original == 0 ? Lcids.English : original });
        }

        foreach (var form in english)
        {
            var es = spanish.FirstOrDefault(s => s.Id == form.Id)?.GetAttributeValue<string>("formxml");
            var en = form.GetAttributeValue<string>("formxml");
            if (!string.IsNullOrWhiteSpace(en) && !string.IsNullOrWhiteSpace(es))
                form["formxml"] = MergeSpanishLabels(en, es);
        }
        return english;
    }

    /// <summary>
    /// Copia al formxml leído en 1033 los <c>&lt;label languagecode="3082"&gt;</c> del formxml leído en 3082, emparejando
    /// tab/section/cell por su atributo id. No toca ningún otro label. Idempotente.
    /// </summary>
    internal static string MergeSpanishLabels(string englishFormXml, string spanishFormXml)
    {
        var en = XDocument.Parse(englishFormXml);
        var es = XDocument.Parse(spanishFormXml);

        var spanishById = es.Descendants()
            .Where(e => e.Name.LocalName is "tab" or "section" or "cell" && e.Attribute("id") is not null)
            .GroupBy(e => (string)e.Attribute("id")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var element in en.Descendants().Where(e => e.Name.LocalName is "tab" or "section" or "cell" && e.Attribute("id") is not null))
        {
            if (!spanishById.TryGetValue((string)element.Attribute("id")!, out var twin))
                continue;
            var spanishLabel = twin.Element("labels")?.Elements("label")
                .FirstOrDefault(l => (string?)l.Attribute("languagecode") == SpanishCode);
            var labels = element.Element("labels");
            if (spanishLabel is null || labels is null)
                continue;

            labels.Elements("label").Where(l => (string?)l.Attribute("languagecode") == SpanishCode).Remove();
            labels.Add(new XElement(spanishLabel));
        }
        return en.ToString(SaveOptions.DisableFormatting);
    }

    private static IReadOnlyList<Entity> Query(ServiceClient client, string entity, bool onlyActive)
    {
        var query = new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid", "formxml", "name"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("objecttypecode", ConditionOperator.Equal, entity),
                    new ConditionExpression("type", ConditionOperator.Equal, 2),
                },
            },
        };
        if (onlyActive)
            query.Criteria.AddCondition("formactivationstate", ConditionOperator.Equal, 1);
        return client.RetrieveMultiple(query).Entities.ToList();
    }
}
