// Edición PURA (sin Dataverse) de systemform.formxml para SchemaAdditionsProvisioner: clona la fila (<row>) que
// contiene el campo ancla y la inserta justo después, apuntando al campo nuevo. Mismo enfoque que el scrub de
// SchemaCleanupProvisioner (que remueve celdas), pero en sentido inverso.

using System.Xml.Linq;

namespace Smelter.Deploy;

internal static class FormXmlEditor
{
    /// <summary>
    /// Agrega <paramref name="newField"/> en una fila nueva justo después de la del <paramref name="anchorField"/>.
    /// Devuelve <c>false</c> (sin tocar nada) si el ancla no está en el form o si el campo nuevo ya está.
    /// </summary>
    internal static bool TryAddCellAfter(XDocument doc, string anchorField, string newField, string label)
    {
        var controls = doc.Descendants("control").ToList();

        if (controls.Any(c => Is(c, newField)))
            return false;

        var anchorControl = controls.FirstOrDefault(c => Is(c, anchorField));
        var anchorRow = anchorControl?.Ancestors("row").FirstOrDefault();
        if (anchorControl is null || anchorRow is null)
            return false;

        var newRow = new XElement(anchorRow);
        var cell = newRow.Descendants("cell").First();
        cell.SetAttributeValue("id", "{" + Guid.NewGuid().ToString("D") + "}");
        foreach (var l in cell.Descendants("label"))
            l.SetAttributeValue("description", label);

        var control = newRow.Descendants("control").First(c => Is(c, anchorField));
        control.SetAttributeValue("id", newField);
        control.SetAttributeValue("datafieldname", newField);

        anchorRow.AddAfterSelf(newRow);
        return true;
    }

    /// <summary>
    /// Quita el campo del form: la celda que contiene su control y, si la fila queda sin celdas, la fila. Devuelve
    /// <c>false</c> (sin tocar nada) si el campo no está en el form — idempotente.
    /// </summary>
    internal static bool TryRemoveField(XDocument doc, string field)
    {
        var controls = doc.Descendants("control").Where(c => Is(c, field)).ToList();
        if (controls.Count == 0)
            return false;

        foreach (var control in controls)
        {
            var cell = control.Ancestors("cell").FirstOrDefault();
            var row = control.Ancestors("row").FirstOrDefault();
            (cell ?? control).Remove();
            if (row is not null && !row.Elements("cell").Any())
                row.Remove();
        }
        return true;
    }

    /// <summary>
    /// Oculta (<c>visible="false"</c> en la celda) o vuelve a mostrar el campo SIN sacar el control del form: la
    /// columna sigue en el form, solo deja de verse. Devuelve <c>false</c> si el campo no está o ya tiene ese estado.
    /// </summary>
    internal static bool TrySetVisible(XDocument doc, string field, bool visible)
    {
        var control = doc.Descendants("control").FirstOrDefault(c => Is(c, field));
        var cell = control?.Ancestors("cell").FirstOrDefault();
        if (cell is null)
            return false;

        var hidden = string.Equals((string?)cell.Attribute("visible"), "false", StringComparison.OrdinalIgnoreCase);
        if (hidden == !visible)
            return false;

        cell.SetAttributeValue("visible", visible ? null : "false");
        return true;
    }

    private static bool Is(XElement control, string field) =>
        string.Equals((string?)control.Attribute("datafieldname"), field, StringComparison.OrdinalIgnoreCase);
}
