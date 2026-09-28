using DataSpace.Core;

namespace DataSpace.Query;

public sealed partial class QueryEngine
{
    private QueryResult ExecuteUnion(DatabaseDocument document, UnionStatement union, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken token, HashSet<string> path, QueryStatistics statistics, QueryExecution execution, EvaluationContext? outer = null)
    {
        var branches = new List<QueryResult>(); var count = 0;
        foreach (var query in union.Queries)
        {
            token.ThrowIfCancellationRequested(); var result = ExecuteSelect(document, query, parameters, token, path, statistics, execution, outer);
            if (branches.Count > 0 && result.Fields.Count != branches[0].Fields.Count) throw new DataSpaceException("UNION branches must have the same number of columns.");
            count = checked(count + result.Records.Count); CheckSize(count); branches.Add(result);
        }
        var fields = branches[0].Fields.Select(TableSchemaDraft.Copy).ToList();
        for (var column = 0; column < fields.Count; column++)
        {
            var types = branches.Select((branch, index) => (branch, plan: union.Queries[index]))
                .Where(item => item.plan.Projections.Count != item.branch.Fields.Count ||
                    item.plan.Projections[column].Expression is not LiteralExpr { Value: null })
                .Select(item => item.branch.Fields[column].Type).Distinct().ToArray();
            if (types.Length == 1) fields[column].Type = types[0];
            else if (types.Length > 1) fields[column].Type = types.All(t => ComparisonFamily(t) == 0) ? FieldType.Decimal : FieldType.LongText;
        }
        var rows = new List<Record>();
        for (var branch = 0; branch < branches.Count; branch++)
        {
            var result = branches[branch];
            foreach (var row in result.Records)
            {
                token.ThrowIfCancellationRequested(); var record = new Record();
                for (var column = 0; column < fields.Count; column++)
                {
                    var target = fields[column]; var source = result.Fields[column];
                    var value = FieldValues.Parse(source, row[source.Name]);
                    if (value is bool boolean && target.Type != FieldType.YesNo && ComparisonFamily(target.Type) == 0)
                        value = boolean ? -1m : 0m;
                    record[target.Name] = FieldValues.Normalize(target, FieldValues.FromObject(value));
                }
                rows.Add(record);
            }
            if (branch > 0 && !union.All[branch - 1])
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                rows = rows.Where(row =>
                {
                    token.ThrowIfCancellationRequested();
                    return seen.Add(SqlValue.Key(fields.Select(f => FieldValues.Parse(f, row[f.Name]))));
                }).ToList();
            }
        }
        if (rows.Count > Options.MaximumResultRows) throw new DataSpaceException("UNION result-row limit exceeded.");
        if (union.Order.Count > 0)
        {
            var scope = new SubqueryScope(this, document, parameters, token, path, statistics, execution, execution.Depth);
            var schema = new EvaluationContext { Parameters = parameters, Outer = outer, Subqueries = scope.Evaluate }; foreach (var field in fields) schema.Values[field.Name] = null;
            foreach (var order in union.Order) scope.Bind(order.Expression, schema);
            var decorated = rows.Select((row, ordinal) =>
            {
                token.ThrowIfCancellationRequested(); var context = new EvaluationContext { Parameters = parameters, Outer = outer, Subqueries = scope.Evaluate };
                foreach (var field in fields) context.Values[field.Name] = FieldValues.Parse(field, row[field.Name]);
                return (Row: row, Ordinal: ordinal, Keys: union.Order.Select(o => o.Expression is LiteralExpr { Value: decimal n } && n == decimal.Truncate(n) && n > 0 && n <= fields.Count
                    ? context.Values[fields[(int)n - 1].Name] : o.Expression.Eval(context)).ToArray());
            }).ToList();
            decorated.Sort((a, b) =>
            {
                token.ThrowIfCancellationRequested();
                for (var i = 0; i < union.Order.Count; i++) { var compare = SqlValue.Compare(a.Keys[i], b.Keys[i]); if (compare != 0) return union.Order[i].Descending ? -Math.Sign(compare) : compare; }
                return a.Ordinal.CompareTo(b.Ordinal);
            });
            rows = decorated.Select(r => r.Row).ToList();
        }
        return new() { Fields = fields, Records = rows };
    }
}
