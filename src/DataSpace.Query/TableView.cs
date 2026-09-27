using DataSpace.Core;

namespace DataSpace.Query;

/// <summary>Identity-preserving table views for editing. Arbitrary SELECT projections remain read-only query results.</summary>
public static class TableView
{
    public static QueryResult Select(DatabaseDocument document, string tableName, string? filter = null,
        string? sortField = null, bool descending = false, string? search = null, CancellationToken cancellationToken = default)
    {
        var table = document.Table(tableName);
        Expr? predicate = null;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            if (filter.Length > 65536) throw new DataSpaceException("Filters are limited to 65,536 characters.");
            var plan = (SelectStatement)new SqlParser("SELECT * FROM " + Names.Quote(table.Name) + " WHERE (" + filter + ");").Parse();
            predicate = plan.Where;
            if (predicate?.Aggregate == true) throw new DataSpaceException("A table filter cannot contain aggregate functions.");
        }
        var matches = new List<Record>();
        foreach (var row in table.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate is not null)
            {
                var context = new EvaluationContext();
                foreach (var field in table.Fields)
                {
                    var value = FieldValues.Parse(field, row[field.Name]);
                    context.Values[field.Name] = value;
                    context.Values[table.Name + "." + field.Name] = value;
                }
                if (!SqlValue.Truth(predicate.Eval(context))) continue;
            }
            if (!string.IsNullOrWhiteSpace(search) && !row.Values.Values.Any(value => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)) continue;
            matches.Add(row);
        }
        IEnumerable<Record> ordered = matches;
        if (sortField is not null)
        {
            var field = table.Field(sortField);
            var comparer = Comparer<object?>.Create(SqlValue.Compare);
            ordered = descending ? matches.OrderByDescending(row => FieldValues.Parse(field, row[field.Name]), comparer)
                : matches.OrderBy(row => FieldValues.Parse(field, row[field.Name]), comparer);
        }
        return new QueryResult
        {
            Fields = table.Fields.Select(TableSchemaDraft.Copy).ToList(),
            Records = ordered.Select(row => new Record { Id = row.Id, Values = new(row.Values, StringComparer.OrdinalIgnoreCase) }).ToList()
        };
    }
}
