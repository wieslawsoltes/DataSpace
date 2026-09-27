namespace DataSpace.Core;

/// <summary>Reusable object creation helpers. Invoke within DatabaseWorkspace.Edit for atomic validation and undo.</summary>
public static class ObjectFactory
{
    public static TableDefinition CreateTable(DatabaseDocument document)
    {
        var table = new TableDefinition
        {
            Name = Names.Available("Table", document.Tables.Select(t => t.Name)),
            DiagramX = 40 + document.Tables.Count % 3 * 310,
            DiagramY = 40 + document.Tables.Count / 3 * 270,
            Fields = [new() { Name = "ID", Type = FieldType.AutoNumber, PrimaryKey = true, Width = 70 }, new() { Name = "Title", Width = 220 }]
        };
        document.Tables.Add(table);
        return table;
    }

    public static QueryDefinition CreateQuery(DatabaseDocument document, string tableName)
    {
        var table = document.Table(tableName);
        var query = new QueryDefinition { Name = Names.Available("Query", document.Queries.Select(q => q.Name)), Sql = "SELECT * FROM " + Names.Quote(table.Name) + ";" };
        document.Queries.Add(query);
        return query;
    }

    public static FormDefinition CreateForm(DatabaseDocument document, string tableName)
    {
        var table = document.Table(tableName);
        var form = new FormDefinition
        {
            Name = Names.Available("Form", document.Forms.Select(f => f.Name)),
            Title = table.Name, Source = table.Name,
            Height = Math.Max(600, 130 + ((table.Fields.Count + 1) / 2) * 82)
        };
        for (var index = 0; index < table.Fields.Count; index++)
        {
            var field = table.Fields[index];
            form.Controls.Add(new LayoutControl
            {
                Kind = field.Type == FieldType.YesNo ? LayoutControlKind.CheckBox : LayoutControlKind.TextBox,
                Field = field.Name, Caption = field.DisplayName,
                X = 35 + index % 2 * 405, Y = 112 + index / 2 * 82, Width = 355, Height = 32
            });
        }
        document.Forms.Add(form);
        return form;
    }

    public static ReportDefinition CreateReport(DatabaseDocument document, string tableName)
    {
        var table = document.Table(tableName);
        var report = new ReportDefinition
        {
            Name = Names.Available("Report", document.Reports.Select(r => r.Name)),
            Title = table.Name, Source = table.Name,
            Fields = table.Fields.Take(6).Select(f => f.Name).ToList(), Landscape = table.Fields.Count > 4
        };
        document.Reports.Add(report);
        return report;
    }

    public static MacroDefinition CreateMacro(DatabaseDocument document, string? tableName = null)
    {
        var macro = new MacroDefinition { Name = Names.Available("Macro", document.Macros.Select(m => m.Name)) };
        if (tableName is not null)
        {
            document.Table(tableName);
            macro.Steps.Add(new() { Action = MacroActionKind.OpenObject, Argument = "Table:" + tableName });
        }
        document.Macros.Add(macro);
        return macro;
    }
}
