// Etiquetas localizadas de ATRIBUTOS de registros (no de metadata): appmodule.name, savedquery.name/description,
// systemform.name. Se leen con RetrieveLocLabelsRequest y se escriben con SetLocLabelsRequest.
//
// Doc (Microsoft Learn): appmodule.name y .description son IsLocalizable = True; SetLocLabels/RetrieveLocLabels soportan
// SavedQuery.Name/Description y SystemForm.Name/Description. El valor 1033 (base) se escribe con Update normal; las demás
// traducciones, con SetLocLabels. Si se actualiza el valor base, las traducciones no cambian (salvo que se vacíe).

using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace Smelter.Deploy;

internal static class LocLabels
{
    /// <summary>Pura: ¿las traducciones actuales (lcid → texto) ya valen exactamente lo deseado en ambos idiomas?</summary>
    internal static bool Matches(IReadOnlyDictionary<int, string> current, Bilingual desired) =>
        current.TryGetValue(Lcids.English, out var en) && en == desired.En
        && current.TryGetValue(Lcids.Spanish, out var es) && es == desired.Es;

    internal static IReadOnlyDictionary<int, string> Get(IOrganizationService service, string entity, Guid id, string attribute)
    {
        var response = (RetrieveLocLabelsResponse)service.Execute(new RetrieveLocLabelsRequest
        {
            EntityMoniker = new EntityReference(entity, id),
            AttributeName = attribute,
            IncludeUnpublished = true,
        });
        var result = new Dictionary<int, string>();
        foreach (var l in response.Label.LocalizedLabels)
            result[l.LanguageCode] = l.Label;
        return result;
    }

    /// <summary>Escribe 1033 + 3082 si difieren. Devuelve <c>true</c> si escribió algo.</summary>
    internal static bool Ensure(IOrganizationService service, string entity, Guid id, string attribute, Bilingual desired)
    {
        if (Matches(Get(service, entity, id, attribute), desired))
            return false;

        service.Execute(new SetLocLabelsRequest
        {
            EntityMoniker = new EntityReference(entity, id),
            AttributeName = attribute,
            Labels = desired.ToLocalizedLabels(),
        });
        return true;
    }
}
