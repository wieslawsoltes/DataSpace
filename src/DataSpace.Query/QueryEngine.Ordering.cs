using DataSpace.Core;

namespace DataSpace.Query;

public sealed partial class QueryEngine
{
    private sealed record SelectedRow(object?[] Values, object?[] Order, int Ordinal);
    private sealed class RowOrdering(IReadOnlyList<Ordering> order, CancellationToken token) : IComparer<SelectedRow>
    {
        private int _comparisons;
        public int Compare(SelectedRow? a, SelectedRow? b)
        {
            if ((++_comparisons & 1023) == 0) token.ThrowIfCancellationRequested();
            if (a is null || b is null) return a is null ? b is null ? 0 : -1 : 1;
            for (var i = 0; i < order.Count; i++)
            {
                var comparison = Math.Sign(SqlValue.Compare(a.Order[i], b.Order[i]));
                if (comparison != 0) return order[i].Descending ? -comparison : comparison;
            }
            return a.Ordinal.CompareTo(b.Ordinal);
        }
    }
    private static FieldDefinition ResultField(string name, object? value, Projection projection,
        List<(Source Source, TableDefinition Table)> sources)
    {
        var field = new FieldDefinition { Name = name, Width = name.Length > 16 ? 220 : 160, Type = InferType(value, projection, sources) };
        if (projection.Expression is NameExpr expression)
        {
            var parts = expression.Name.Split('.');
            foreach (var source in sources)
            {
                if (parts.Length == 2 && !Names.Equal(parts[0], source.Source.Alias)) continue;
                if (source.Table.Fields.FirstOrDefault(f => Names.Equal(f.Name, parts[^1])) is { } original)
                { field.MaxLength = original.MaxLength; break; }
            }
        }
        return field;
    }
}
