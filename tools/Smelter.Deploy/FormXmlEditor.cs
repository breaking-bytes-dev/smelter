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
    internal static bool TryAddCellAfter(XDocument doc, string anchorField, string newField, string label,
        string? classId = null, bool? disabled = null)
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

        // Otro tipo de control (número, choice, multilínea ...): el clon del ancla trae el classid del ancla.
        if (classId is not null)
        {
            control.SetAttributeValue("classid", classId);
            control.Element("parameters")?.Remove();
        }
        if (disabled is { } d)
            control.SetAttributeValue("disabled", d ? "true" : "false");

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

    /// <summary>
    /// Deja el control en solo lectura (<c>disabled="true"</c>) o editable. Es UX, no seguridad: no impide escribir
    /// por API ni por script. Devuelve <c>false</c> si el campo no está o ya tiene ese estado.
    /// </summary>
    internal static bool TrySetDisabled(XDocument doc, string field, bool disabled)
    {
        var control = doc.Descendants("control").FirstOrDefault(c => Is(c, field));
        if (control is null)
            return false;

        var current = string.Equals((string?)control.Attribute("disabled"), "true", StringComparison.OrdinalIgnoreCase);
        if (current == disabled && (disabled || control.Attribute("disabled") is not null))
            return false;

        control.SetAttributeValue("disabled", disabled ? "true" : "false");
        return true;
    }

    private const string SubgridClassId = "{E7A81278-8635-4D9E-8D4D-59480B391C5B}";

    /// <summary>
    /// Agrega una pestaña nueva (al final) con un subgrid de registros relacionados: tab → column 100% → section →
    /// celda con el control subgrid (mismo formato que el form del Import Template). Idempotente por el id del
    /// subgrid; devuelve <c>false</c> si ya existe o si el form no tiene pestañas.
    /// </summary>
    internal static bool TryAddSubgridTab(XDocument doc, string tabLabel, string subgridId, string relationshipName,
        string targetEntity, string viewId)
    {
        if (doc.Descendants("control").Any(c => string.Equals((string?)c.Attribute("id"), subgridId, StringComparison.OrdinalIgnoreCase)))
            return false;

        var lastTab = doc.Descendants("tab").LastOrDefault();
        if (lastTab is null)
            return false;

        static string NewId() => "{" + Guid.NewGuid().ToString("D") + "}";
        static XElement Labels(string text) =>
            new("labels", new XElement("label", new XAttribute("description", text), new XAttribute("languagecode", "1033")));

        const int rowSpan = 8;
        var rows = new XElement("rows",
            new XElement("row",
                new XElement("cell",
                    new XAttribute("id", NewId()),
                    new XAttribute("rowspan", rowSpan), new XAttribute("colspan", 1), new XAttribute("auto", "false"),
                    Labels(tabLabel),
                    new XElement("control",
                        new XAttribute("indicationOfSubgrid", "true"),
                        new XAttribute("id", subgridId),
                        new XAttribute("classid", SubgridClassId),
                        new XElement("parameters",
                            new XElement("RecordsPerPage", 10),
                            new XElement("AutoExpand", "Fixed"),
                            new XElement("EnableQuickFind", "false"),
                            new XElement("EnableViewPicker", "false"),
                            new XElement("EnableChartPicker", "false"),
                            new XElement("ChartGridMode", "Grid"),
                            new XElement("RelationshipName", relationshipName),
                            new XElement("TargetEntityType", targetEntity),
                            new XElement("ViewId", viewId),
                            new XElement("ViewIds", viewId))))));
        for (var i = 1; i < rowSpan; i++)
            rows.Add(new XElement("row"));

        var tab = new XElement("tab",
            new XAttribute("verticallayout", "true"),
            new XAttribute("id", NewId()),
            new XAttribute("IsUserDefined", "1"),
            Labels(tabLabel),
            new XElement("columns",
                new XElement("column", new XAttribute("width", "100%"),
                    new XElement("sections",
                        new XElement("section",
                            new XAttribute("showlabel", "false"), new XAttribute("showbar", "false"),
                            new XAttribute("IsUserDefined", "0"), new XAttribute("id", NewId()),
                            Labels(tabLabel),
                            rows)))));

        lastTab.AddAfterSelf(tab);
        return true;
    }

    /// <summary>
    /// Related-records filtering de un lookup: sólo ofrece registros que comparten, vía <paramref name="filterRelationship"/>,
    /// el valor del campo <paramref name="dependentAttribute"/> (tipo lookup) del mismo form. Escribe
    /// FilterRelationshipName / DependentAttributeName / DependentAttributeType en <c>&lt;parameters&gt;</c> conservando
    /// el resto. Devuelve <c>false</c> si el campo no está o ya tiene exactamente esos valores.
    /// </summary>
    internal static bool TrySetLookupFilter(XDocument doc, string field, string filterRelationship, string dependentAttribute)
    {
        var control = doc.Descendants("control").FirstOrDefault(c => Is(c, field));
        if (control is null)
            return false;

        var parameters = control.Element("parameters");
        if (parameters is null)
        {
            parameters = new XElement("parameters");
            control.Add(parameters);
        }

        var changed = SetParameter(parameters, "FilterRelationshipName", filterRelationship);
        changed |= SetParameter(parameters, "DependentAttributeName", dependentAttribute);
        changed |= SetParameter(parameters, "DependentAttributeType", "lookup");
        return changed;
    }

    private static bool SetParameter(XElement parameters, string name, string value)
    {
        var existing = parameters.Element(name);
        if (existing is null)
        {
            parameters.Add(new XElement(name, value));
            return true;
        }
        if (existing.Value == value)
            return false;
        existing.Value = value;
        return true;
    }

    private static bool Is(XElement control, string field) =>
        string.Equals((string?)control.Attribute("datafieldname"), field, StringComparison.OrdinalIgnoreCase);
}
