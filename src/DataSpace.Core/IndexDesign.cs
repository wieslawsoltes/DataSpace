namespace DataSpace.Core;

/// <summary>Detached ordered single/composite index definitions. Apply validates existing data and relationship dependencies.</summary>
public sealed class IndexDesign
{
    public string TableName { get; }
    public long Revision { get; private set; }
    public List<IndexDefinition> Indexes { get; }
    public IndexDesign(DatabaseDocument document, string tableName)
    {
        TableName = document.Table(tableName).Name; Revision = document.Revision;
        Indexes = document.Table(tableName).Indexes.Select(Copy).ToList();
    }
    public void Apply(DatabaseWorkspace workspace)
    {
        var snapshot = Indexes.Select(Copy).ToList();
        if (snapshot.Count > 128) throw new DataSpaceException("The index designer supports at most 128 indexes per table.");
        foreach (var index in snapshot)
        {
            if (index.Fields.Count == 0 || index.Fields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != index.Fields.Count)
                throw new DataSpaceException("Each index requires one or more distinct fields.");
        }
        workspace.Edit("Edit indexes", document => document.Table(TableName).Indexes = snapshot, Revision);
        Revision = workspace.Document.Revision;
    }
    private static IndexDefinition Copy(IndexDefinition index) => new() { Name = index.Name, Unique = index.Unique, Fields = new(index.Fields) };
}
