namespace DataSpace.Core;

/// <summary>Finds a record without copying a view into a temporary list.</summary>
public static class RecordIdentity
{
    public static int IndexOf(IReadOnlyList<Record> records, string identity)
    {
        if (records is IRecordIndex indexed) return indexed.IndexOfRecord(identity);
        for (var index = 0; index < records.Count; index++) if (records[index].Id == identity) return index;
        return -1;
    }
}
