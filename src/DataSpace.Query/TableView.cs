using System.Collections;
using DataSpace.Core;

namespace DataSpace.Query;

/// <summary>Snapshot-backed view with bounded lazy record materialization and stable record identity.</summary>
public sealed class VirtualTableView : IReadOnlyList<Record>, IRecordIndex
{
    private readonly IReadOnlyList<Record> _source;
    private readonly int[]? _order;
    private readonly Dictionary<int, Record> _cache = new();
    private readonly Queue<int> _fifo = new();
    public IReadOnlyList<FieldDefinition> Fields { get; }
    public int Count => _order?.Length ?? _source.Count;
    public int MaterializedRecordCount { get; private set; }
    public int CachedRecordCount => _cache.Count;
    public const int CacheCapacity = 256;
    internal VirtualTableView(TableDefinition table, int[]? order)
    { _source = table.Records; _order = order; Fields = table.Fields.Select(TableSchemaDraft.Copy).ToArray(); }
    public Record this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_cache.TryGetValue(index, out var record)) return record;
            record = DocumentSnapshot.CopyRecord(_source[_order is null ? index : _order[index]]);
            if (_cache.Count == CacheCapacity) _cache.Remove(_fifo.Dequeue());
            _cache.Add(index, record); _fifo.Enqueue(index); MaterializedRecordCount++;
            return record;
        }
    }
    public int IndexOfRecord(string identity)
    {
        for (var index = 0; index < Count; index++) if (_source[_order is null ? index : _order[index]].Id == identity) return index;
        return -1;
    }
    public IReadOnlyList<Record> ReadPage(int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset); ArgumentOutOfRangeException.ThrowIfNegative(count);
        var result = new Record[Math.Min(count, Math.Max(0, Count - offset))];
        for (var index = 0; index < result.Length; index++) result[index] = this[offset + index];
        return result;
    }
    public IEnumerator<Record> GetEnumerator() { for (var index = 0; index < Count; index++) yield return this[index]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Identity-preserving editable views. Open is lazy; Select is the compatible materializing API.</summary>
public static class TableView
{
    public static VirtualTableView Open(DatabaseDocument document, string tableName, string? filter = null,
        string? sortField = null, bool descending = false, string? search = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var table = document.Table(tableName);
        Expr? predicate = null;
        var context = new EvaluationContext();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            if (filter.Length > 65536) throw new DataSpaceException("Filters are limited to 65,536 characters.");
            predicate = ((SelectStatement)new SqlParser("SELECT * FROM " + Names.Quote(table.Name) + " WHERE (" + filter + ");").Parse()).Where;
            if (predicate?.Aggregate == true) throw new DataSpaceException("A table filter cannot contain aggregate functions.");
            foreach (var field in table.Fields) { context.Values[field.Name] = null; context.Values[table.Name + "." + field.Name] = null; }
            if (predicate is not null) foreach (var name in ExpressionAnalysis.Names(predicate)) context.Resolve(name.Name, name.Parameter);
        }
        var sort = sortField is null ? null : table.Field(sortField);
        var searching = !string.IsNullOrWhiteSpace(search);
        if (predicate is null && sort is null && !searching) return new(table, null);
        var referenced = predicate is null ? new HashSet<string>() : ExpressionAnalysis.Names(predicate).Select(n => n.Name.Split('.').Last()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bindings = table.Fields.Where(f => referenced.Contains(f.Name)).Select(f => (Field: f, Qualified: table.Name + "." + f.Name)).ToArray();
        var matches = new List<int>();
        for (var index = 0; index < table.Records.Count; index++)
        {
            if ((index & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
            var row = table.Records[index];
            if (predicate is not null)
            {
                // Reuse one scalar environment; no dictionary or full-row clone for each filter comparison.
                foreach (var binding in bindings)
                {
                    var value = FieldValues.Parse(binding.Field, row[binding.Field.Name]);
                    context.Values[binding.Field.Name] = value; context.Values[binding.Qualified] = value;
                }
                if (!SqlValue.Truth(predicate.Eval(context))) continue;
            }
            if (searching && !row.Values.Values.Any(value => value?.Contains(search!, StringComparison.OrdinalIgnoreCase) == true)) continue;
            matches.Add(index);
        }
        var order = matches.ToArray();
        if (sort is not null)
        {
            var keys = new object?[table.Records.Count];
            foreach (var index in order)
            { if ((index & 127) == 0) cancellationToken.ThrowIfCancellationRequested(); keys[index] = FieldValues.Parse(sort, table.Records[index][sort.Name]); }
            var comparisons = 0;
            Array.Sort(order, (a, b) =>
            {
                if ((++comparisons & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var compare = SqlValue.Compare(keys[a], keys[b]);
                return compare == 0 ? a.CompareTo(b) : descending ? -Math.Sign(compare) : compare;
            });
        }
        cancellationToken.ThrowIfCancellationRequested(); return new(table, order);
    }
    public static QueryResult Select(DatabaseDocument document, string tableName, string? filter = null,
        string? sortField = null, bool descending = false, string? search = null, CancellationToken cancellationToken = default)
    {
        var view = Open(document, tableName, filter, sortField, descending, search, cancellationToken);
        var rows = new List<Record>(view.Count);
        for (var index = 0; index < view.Count; index++) { if ((index & 127) == 0) cancellationToken.ThrowIfCancellationRequested(); rows.Add(view[index]); }
        return new() { Fields = view.Fields.ToList(), Records = rows };
    }
}
