namespace DataSpace.Core;

/// <summary>A single-writer, copy-on-write transactional document with bounded undo/redo.</summary>
public sealed class DatabaseWorkspace
{
    private readonly LinkedList<(DatabaseDocument Document, string Label)> _undo = new();
    private readonly Stack<(DatabaseDocument Document, string Label)> _redo = new();
    private bool _editing;
    public DatabaseDocument Document { get; private set; }
    public int HistoryLimit { get; init; } = 32;
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public string UndoLabel => _undo.Last?.Value.Label ?? "";
    public event EventHandler? Changed;
    public DatabaseWorkspace(DatabaseDocument document) { Document = DocumentCodec.Clone(document); }

    /// <summary>Mutate only the supplied draft. Constraint errors leave the live document and history unchanged.</summary>
    public void Edit(string label, Action<DatabaseDocument> edit, long? expectedRevision = null)
    {
        if (_editing) throw new DataSpaceException("Nested transactions are not supported.");
        if (expectedRevision is { } revision && revision != Document.Revision) throw new DataSpaceException("The database changed. Refresh before applying this edit.");
        _editing = true;
        try
        {
            var draft = DocumentCodec.Clone(Document);
            edit(draft);
            SchemaValidator.Validate(draft);
            draft.Revision = checked(Document.Revision + 1);
            _undo.AddLast((Document, label));
            while (_undo.Count > HistoryLimit) _undo.RemoveFirst();
            _redo.Clear();
            Document = draft;
        }
        finally { _editing = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Undo()
    {
        if (_undo.Last is not { } item) return;
        _redo.Push((Document, item.Value.Label));
        var next = DocumentCodec.Clone(item.Value.Document);
        next.Revision = checked(Document.Revision + 1);
        _undo.RemoveLast(); Document = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Redo()
    {
        if (!_redo.TryPop(out var item)) return;
        _undo.AddLast((Document, item.Label));
        var next = DocumentCodec.Clone(item.Document);
        next.Revision = checked(Document.Revision + 1); Document = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Replace(DatabaseDocument document)
    {
        var next = DocumentCodec.Clone(document);
        Document = next; _undo.Clear(); _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public static class RecordOperations
{
    public static Record Insert(TableDefinition table, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var name in values.Keys) table.Field(name);
        var record = new Record();
        foreach (var field in table.Fields)
        {
            var pair = values.FirstOrDefault(p => Names.Equal(p.Key, field.Name));
            var value = pair.Key is not null ? pair.Value : field.DefaultValue;
            if (field.Type == FieldType.AutoNumber && value is null) value = (table.NextAutoNumber++).ToString(FieldValues.Culture);
            if (field.Type == FieldType.Guid && value is null) value = System.Guid.NewGuid().ToString();
            record[field.Name] = FieldValues.Normalize(field, value);
        }
        table.Records.Add(record);
        return record;
    }
    public static void Update(DatabaseDocument document, string tableName, string recordId, IReadOnlyDictionary<string, string?> values)
    {
        var table = document.Table(tableName);
        var record = table.Records.FirstOrDefault(r => r.Id == recordId) ?? throw new DataSpaceException("The record no longer exists.");
        var pending = new Queue<(string Table, string Id, string Field, string? Value)>();
        foreach (var (field, value) in values) pending.Enqueue((tableName, recordId, table.Field(field).Name, value));
        var updates = 0;
        while (pending.TryDequeue(out var update))
        {
            if (++updates > 100000) throw new DataSpaceException("Cascade update limit exceeded.");
            var target = document.Table(update.Table);
            var row = target.Records.First(r => r.Id == update.Id);
            var value = FieldValues.Normalize(target.Field(update.Field), update.Value);
            var previous = row[update.Field];
            if (Names.Equal(previous, value)) { row[update.Field] = value; continue; }
            foreach (var relation in document.Relationships.Where(r => r.EnforceIntegrity && r.CascadeUpdate && Names.Equal(r.ParentTable, target.Name) && Names.Equal(r.ParentField, update.Field)))
                if (previous is not null)
                    foreach (var child in document.Table(relation.ChildTable).Records.Where(r => Names.Equal(r[relation.ChildField], previous)).ToArray())
                        pending.Enqueue((relation.ChildTable, child.Id, relation.ChildField, value));
            row[update.Field] = value;
        }
    }
    public static void Delete(DatabaseDocument document, string tableName, IEnumerable<string> recordIds)
    {
        var pending = new Queue<(string Table, string Id)>(recordIds.Select(id => (tableName, id)));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out var item))
        {
            if (!visited.Add(item.Table + ":" + item.Id)) continue;
            var table = document.Table(item.Table);
            var record = table.Records.FirstOrDefault(r => r.Id == item.Id);
            if (record is null) continue;
            foreach (var relation in document.Relationships.Where(r => r.EnforceIntegrity && r.CascadeDelete && Names.Equal(r.ParentTable, table.Name)))
                if (record[relation.ParentField] is { } key)
                    foreach (var child in document.Table(relation.ChildTable).Records.Where(r => Names.Equal(r[relation.ChildField], key)).ToArray())
                        pending.Enqueue((relation.ChildTable, child.Id));
            table.Records.Remove(record);
        }
    }
    public static void AddField(TableDefinition table, FieldDefinition field)
    {
        Names.Validate(field.Name);
        if (table.Fields.Any(f => Names.Equal(f.Name, field.Name))) throw new DataSpaceException("A field with this name already exists.");
        table.Fields.Add(field);
        foreach (var row in table.Records)
        {
            var value = field.Type == FieldType.AutoNumber ? (table.NextAutoNumber++).ToString(FieldValues.Culture) : field.DefaultValue;
            row[field.Name] = FieldValues.Normalize(field, value);
        }
    }
    public static void RenameField(DatabaseDocument document, string tableName, string oldName, string newName)
    {
        Names.Validate(newName);
        var table = document.Table(tableName);
        if (!Names.Equal(oldName, newName) && table.Fields.Any(f => Names.Equal(f.Name, newName))) throw new DataSpaceException("Field name already exists.");
        table.Field(oldName).Name = newName;
        foreach (var row in table.Records) { var value = row[oldName]; row.Values.Remove(oldName); row[newName] = value; }
        foreach (var index in table.Indexes) for (var i = 0; i < index.Fields.Count; i++) if (Names.Equal(index.Fields[i], oldName)) index.Fields[i] = newName;
        foreach (var relation in document.Relationships)
        {
            if (Names.Equal(relation.ParentTable, tableName) && Names.Equal(relation.ParentField, oldName)) relation.ParentField = newName;
            if (Names.Equal(relation.ChildTable, tableName) && Names.Equal(relation.ChildField, oldName)) relation.ChildField = newName;
        }
        foreach (var form in document.Forms.Where(f => Names.Equal(f.Source, tableName)))
            foreach (var control in form.Controls.Where(c => Names.Equal(c.Field, oldName))) control.Field = newName;
        foreach (var report in document.Reports.Where(r => Names.Equal(r.Source, tableName)))
            for (var i = 0; i < report.Fields.Count; i++) if (Names.Equal(report.Fields[i], oldName)) report.Fields[i] = newName;
        // SQL text is deliberately not rewritten by substring; query dependencies must be reviewed after schema changes.
    }
}
