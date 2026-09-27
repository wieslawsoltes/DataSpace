namespace DataSpace.Core;

/// <summary>Copies the model without serializing a JSON string. Strings are immutable; all mutable containers are copied.</summary>
public static class DocumentSnapshot
{
    public static DatabaseDocument Copy(DatabaseDocument source) => Copy(source, false);

    // Only workspace record transactions may share unchanged records. General edits always deep-copy them.
    internal static DatabaseDocument Copy(DatabaseDocument source, bool shareRecords)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new DatabaseDocument
        {
            FormatVersion = source.FormatVersion, Name = source.Name, Revision = source.Revision,
            Tables = source.Tables.Select(t => new TableDefinition
            {
                Name = t.Name, Description = t.Description, NextAutoNumber = t.NextAutoNumber,
                DiagramX = t.DiagramX, DiagramY = t.DiagramY,
                Fields = t.Fields.Select(TableSchemaDraft.Copy).ToList(),
                Records = shareRecords ? new(t.Records) : t.Records.Select(CopyRecord).ToList(),
                Indexes = t.Indexes.Select(i => new IndexDefinition { Name = i.Name, Unique = i.Unique, Fields = new(i.Fields) }).ToList()
            }).ToList(),
            Relationships = source.Relationships.Select(r => new RelationshipDefinition
            {
                Name = r.Name, ParentTable = r.ParentTable, ParentField = r.ParentField,
                ChildTable = r.ChildTable, ChildField = r.ChildField, EnforceIntegrity = r.EnforceIntegrity,
                CascadeUpdate = r.CascadeUpdate, CascadeDelete = r.CascadeDelete
            }).ToList(),
            Queries = source.Queries.Select(q => new QueryDefinition
            {
                Name = q.Name, Sql = q.Sql, DesignerState = q.DesignerState,
                Parameters = new(q.Parameters, StringComparer.OrdinalIgnoreCase)
            }).ToList(),
            Forms = source.Forms.Select(f => new FormDefinition
            {
                Name = f.Name, Title = f.Title, Source = f.Source, Width = f.Width, Height = f.Height,
                Controls = f.Controls.Select(c => new LayoutControl
                {
                    Id = c.Id, Kind = c.Kind, Field = c.Field, Caption = c.Caption,
                    X = c.X, Y = c.Y, Width = c.Width, Height = c.Height, FontSize = c.FontSize
                }).ToList()
            }).ToList(),
            Reports = source.Reports.Select(r => new ReportDefinition
            {
                Name = r.Name, Title = r.Title, Source = r.Source, Landscape = r.Landscape,
                ShowTotals = r.ShowTotals, Fields = new(r.Fields)
            }).ToList(),
            Macros = source.Macros.Select(m => new MacroDefinition
            {
                Name = m.Name, Steps = m.Steps.Select(s => new MacroStep { Action = s.Action, Argument = s.Argument }).ToList()
            }).ToList()
        };
    }
    public static Record CopyRecord(Record record) => new() { Id = record.Id, Values = new(record.Values, StringComparer.OrdinalIgnoreCase) };
}
