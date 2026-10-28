// Edición PURA (sin Dataverse) del layoutxml + fetchxml de una vista (savedquery): agrega una columna justo después de
// otra. Idempotente. Mismo espíritu que FormXmlEditor, para vistas.

using System.Xml.Linq;

namespace Smelter.Deploy;

internal static class ViewXmlEditor
{
    /// <summary>
    /// Agrega <paramref name="column"/> al layout (cell tras <paramref name="afterColumn"/>, o al final si el ancla no
    /// está) y al fetch (attribute de la entidad raíz). Devuelve los XML resultantes y si cambió algo.
    /// </summary>
    internal static (string LayoutXml, string FetchXml, bool Changed) TryAddColumn(
        string layoutXml, string fetchXml, string afterColumn, string column, int width)
    {
        var layout = XDocument.Parse(layoutXml);
        var fetch = XDocument.Parse(fetchXml);
        var changed = false;

        var cells = layout.Descendants("cell").ToList();
        if (!cells.Any(c => Is(c, "name", column)))
        {
            var cell = new XElement("cell", new XAttribute("name", column), new XAttribute("width", width));
            var anchor = cells.FirstOrDefault(c => Is(c, "name", afterColumn));
            if (anchor is not null) anchor.AddAfterSelf(cell);
            else (cells.LastOrDefault()?.Parent ?? layout.Descendants("row").First()).Add(cell);
            changed = true;
        }

        var entity = fetch.Descendants("entity").First();
        if (!entity.Elements("attribute").Any(a => Is(a, "name", column)))
        {
            var attribute = new XElement("attribute", new XAttribute("name", column));
            var anchor = entity.Elements("attribute").FirstOrDefault(a => Is(a, "name", afterColumn));
            if (anchor is not null) anchor.AddAfterSelf(attribute);
            else entity.Elements("attribute").LastOrDefault()?.AddAfterSelf(attribute);
            if (!entity.Elements("attribute").Any(a => Is(a, "name", column))) entity.AddFirst(attribute);
            changed = true;
        }

        return changed
            ? (layout.ToString(SaveOptions.DisableFormatting), fetch.ToString(SaveOptions.DisableFormatting), true)
            : (layoutXml, fetchXml, false);
    }

    /// <summary>
    /// Deja el fetch con UN solo &lt;order&gt; (el pedido) en la entidad raíz: reemplaza los que hubiera. Idempotente.
    /// </summary>
    internal static (string FetchXml, bool Changed) TrySetOrder(string fetchXml, string attribute, bool descending)
    {
        var fetch = XDocument.Parse(fetchXml);
        var entity = fetch.Descendants("entity").First();
        var orders = entity.Elements("order").ToList();
        var wanted = descending ? "true" : "false";

        if (orders.Count == 1 && Is(orders[0], "attribute", attribute)
            && string.Equals((string?)orders[0].Attribute("descending") ?? "false", wanted, StringComparison.OrdinalIgnoreCase))
            return (fetchXml, false);

        var order = new XElement("order", new XAttribute("attribute", attribute), new XAttribute("descending", wanted));
        if (orders.Count > 0) orders[0].AddBeforeSelf(order); // misma posición que el orden anterior (el XSD de fetch acepta order tras attribute)
        else (entity.Elements("attribute").LastOrDefault() ?? (XNode?)null)?.AddAfterSelf(order);
        if (order.Parent is null) entity.AddFirst(order);
        foreach (var o in orders) o.Remove();
        return (fetch.ToString(SaveOptions.DisableFormatting), true);
    }

    private static bool Is(XElement e, string attribute, string value) =>
        string.Equals((string?)e.Attribute(attribute), value, StringComparison.OrdinalIgnoreCase);
}
