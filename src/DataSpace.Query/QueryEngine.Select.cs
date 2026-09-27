using DataSpace.Core;

namespace DataSpace.Query;

public sealed partial class QueryEngine
{
    private QueryResult Read(DatabaseDocument document, Statement plan, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken token, HashSet<string> path, QueryStatistics statistics) => plan switch
    {
        SelectStatement select => ExecuteSelect(document, select, parameters, token, path, statistics),
        TransformStatement transform => ExecuteTransform(document, transform, parameters, token, path, statistics),
        UnionStatement union => ExecuteUnion(document, union, parameters, token, path, statistics),
        _ => throw new DataSpaceException("A record source must be a SELECT, UNION or TRANSFORM query.")
    };
    private TableDefinition ResolveSource(DatabaseDocument document, string name, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken token, HashSet<string> path, QueryStatistics statistics)
    {
        var table = document.Tables.FirstOrDefault(t => Names.Equal(t.Name, name)); if (table is not null) return table;
        var query = document.Queries.FirstOrDefault(q => Names.Equal(q.Name, name)) ?? throw new DataSpaceException($"Table or query '{name}' does not exist.");
        if (path.Count >= Options.MaximumSourceDepth || !path.Add(query.Name)) throw new DataSpaceException("Circular or excessively deep saved-query source: " + name);
        try
        {
            var args = query.Parameters.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in parameters) args[pair.Key] = pair.Value;
            var result = Read(document, Parse(query.Sql), args, token, path, statistics);
            return new() { Name = query.Name, Fields = result.Fields, Records = result.Records };
        }
        finally { path.Remove(query.Name); }
    }
    private IEnumerable<EvaluationContext> SourceRows(TableDefinition table, string alias, IReadOnlyDictionary<string, object?> parameters, CancellationToken token, QueryStatistics statistics)
    {
        var count = 0;
        foreach (var record in table.Records)
        {
            token.ThrowIfCancellationRequested(); CheckSize(++count); statistics.SourceRowsRead++;
            yield return AddSource(new EvaluationContext { Parameters = parameters }, table, alias, record);
        }
    }
    private QueryResult ExecuteSelect(DatabaseDocument document, SelectStatement plan, IReadOnlyDictionary<string, object?> parameters,
        CancellationToken token, HashSet<string> path, QueryStatistics statistics)
    {
        token.ThrowIfCancellationRequested();
        var sources = new List<(Source Source, TableDefinition Table)>();
        if (plan.Source is { } from) sources.Add((from, ResolveSource(document, from.Table, parameters, token, path, statistics)));
        foreach (var join in plan.Joins) sources.Add((join.Source, ResolveSource(document, join.Source.Table, parameters, token, path, statistics)));
        if (sources.Select(s => s.Source.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count) throw new DataSpaceException("Duplicate table alias.");
        var projections = Expand(plan.Projections, sources);
        var schema = new EvaluationContext { Parameters = parameters };
        foreach (var source in sources) schema = AddSource(schema, source.Table, source.Source.Alias, null);
        void Bind(Expr expression, EvaluationContext context) { foreach (var name in ExpressionAnalysis.Names(expression)) context.Resolve(name.Name, name.Parameter); }
        foreach (var projection in projections) Bind(projection.Expression, schema);
        if (plan.Where is { } predicate)
        { if (predicate.Aggregate) throw new DataSpaceException("Aggregates belong in HAVING, not WHERE."); Bind(predicate, schema); }
        foreach (var group in plan.Groups) { if (group.Aggregate) throw new DataSpaceException("GROUP BY cannot contain aggregates."); Bind(group, schema); }
        foreach (var join in plan.Joins)
        { if (join.Condition is { } on) { if (on.Aggregate) throw new DataSpaceException("Join conditions cannot contain aggregates."); Bind(on, schema); } }
        var names = OutputNames(projections);
        var aliases = schema.Clone(); foreach (var name in names) { aliases.Values[name] = null; aliases.Ambiguous.Remove(name); }
        if (plan.Having is { } h) Bind(h, aliases);
        foreach (var order in plan.Order) Bind(order.Expression, aliases);
        IEnumerable<EvaluationContext> rows = plan.Source is null ? [new EvaluationContext { Parameters = parameters }]
            : SourceRows(sources[0].Table, sources[0].Source.Alias, parameters, token, statistics);
        for (var index = 0; index < plan.Joins.Count; index++)
            rows = JoinRows(rows, plan.Joins[index], sources[index + 1].Table, sources.Take(index + 1).ToList(), parameters, token, statistics);
        if (plan.Where is { } where) rows = rows.Where(row => { token.ThrowIfCancellationRequested(); return SqlValue.Truth(where.Eval(row)); });
        var grouped = plan.Groups.Count > 0 || projections.Any(p => p.Expression.Aggregate) || plan.Having?.Aggregate == true;
        if (grouped)
        {
            if (projections.Any(p => !p.Expression.GroupSafe(plan.Groups))) throw new DataSpaceException("Every selected field must be grouped or aggregated.");
            if (Options.EnableStreamingAggregates) rows = StreamGroups(rows, plan, projections, parameters, token, statistics);
            else
            {
            var buffered = rows.ToList(); CheckSize(buffered.Count);
            statistics.BufferedAggregateRows = Math.Max(statistics.BufferedAggregateRows, buffered.Count);
            if (plan.Groups.Count == 0)
            {
                var context = buffered.FirstOrDefault()?.Clone() ?? new EvaluationContext { Parameters = parameters };
                context.Group = buffered; rows = [context];
            }
            else rows = buffered.GroupBy(row => { token.ThrowIfCancellationRequested(); return SqlValue.Key(plan.Groups.Select(g => g.Eval(row))); })
                .Select(group => { var context = group.First().Clone(); context.Group = group.ToList(); return context; });
            }
        }
        else if (plan.Having is not null) throw new DataSpaceException("HAVING requires grouping or aggregates.");
        var selected = new List<SelectedRow>();
        var ordering = new RowOrdering(plan.Order, token);
        var capacity = plan.Limit is { } requestedLimit ? (long)plan.Offset + requestedLimit : 0;
        PriorityQueue<SelectedRow, SelectedRow>? top = Options.EnableTopKSort && plan.Order.Count > 0 &&
            plan.Limit is > 0 && capacity <= Options.MaximumIntermediateRows
            ? new(Comparer<SelectedRow>.Create((a, b) => ordering.Compare(b, a))) : null;
        var seen = new HashSet<string>(StringComparer.Ordinal); var skipped = 0; var ordinal = 0;
        var sorting = plan.Order.Count > 0;
        // Without ordering, stop once TOP/LIMIT is satisfied; do not clone unseen rows.
        if (plan.Limit != 0)
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested(); var values = projections.Select(p => p.Expression.Eval(row)).ToArray();
            var context = row;
            if (plan.Having is not null || sorting)
            {
                context = row.Clone();
                for (var i = 0; i < names.Count; i++) { context.Values[names[i]] = values[i]; context.Ambiguous.Remove(names[i]); }
            }
            if (plan.Having is { } having && !SqlValue.Truth(having.Eval(context))) continue;
            if (plan.Distinct && !seen.Add(SqlValue.Key(values))) continue;
            if (!sorting && skipped++ < plan.Offset) continue;
            var orderValues = plan.Order.Select(o => o.Expression is LiteralExpr { Value: decimal n } && n == decimal.Truncate(n) && n > 0 && n <= values.Length
                ? values[(int)n - 1] : o.Expression.Eval(context)).ToArray();
            var item = new SelectedRow(values, orderValues, ordinal++);
            if (sorting)
            {
                statistics.SortCandidateRows++;
                if (top is not null)
                {
                    if (top.Count < capacity) top.Enqueue(item, item);
                    else if (ordering.Compare(item, top.Peek()) < 0) top.DequeueEnqueue(item, item);
                    statistics.PeakSortRows = Math.Max(statistics.PeakSortRows, top.Count);
                }
                else
                {
                    selected.Add(item); CheckSize(selected.Count);
                    statistics.PeakSortRows = Math.Max(statistics.PeakSortRows, selected.Count);
                }
            }
            else
            {
                selected.Add(item);
                if (selected.Count > Options.MaximumResultRows) throw new DataSpaceException("Result-row limit exceeded. Use TOP or LIMIT.");
                if (plan.Limit is { } limit && selected.Count >= limit) break;
            }
        }
        if (sorting)
        {
            if (top is not null) selected = top.UnorderedItems.Select(i => i.Element).ToList();
            selected.Sort(ordering);
            selected = selected.Skip(plan.Offset).Take(plan.Limit ?? int.MaxValue).ToList();
        }
        if (selected.Count > Options.MaximumResultRows) throw new DataSpaceException("Result-row limit exceeded. Use TOP or LIMIT.");
        var fields = names.Select((name, i) => ResultField(name, selected.Select(r => r.Values[i]).FirstOrDefault(v => v is not null), projections[i], sources)).ToList();
        return new()
        {
            Fields = fields,
            Records = selected.Select(row => new Record { Values = names.Select((name, i) => KeyValuePair.Create(name, FieldValues.FromObject(row.Values[i]))).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase) }).ToList()
        };
    }
    private IEnumerable<EvaluationContext> JoinRows(IEnumerable<EvaluationContext> left, Join join, TableDefinition right,
        List<(Source Source, TableDefinition Table)> earlier, IReadOnlyDictionary<string, object?> parameters, CancellationToken token, QueryStatistics statistics)
    {
        CheckSize(right.Records.Count);
        var keys = new List<(NameExpr Left, NameExpr Right)>();
        if (Options.EnableHashJoins && join.Condition is not null)
        foreach (var part in ExpressionAnalysis.Conjuncts(join.Condition))
        {
            if (part is not BinaryExpr { Op: "=", Left: NameExpr { Parameter: false } a, Right: NameExpr { Parameter: false } b }) continue;
            if (a.Name.StartsWith(join.Source.Alias + ".", StringComparison.OrdinalIgnoreCase)) (a, b) = (b, a);
            if (!b.Name.StartsWith(join.Source.Alias + ".", StringComparison.OrdinalIgnoreCase)) continue;
            var pieces = a.Name.Split('.'); if (pieces.Length != 2) continue;
            var previous = earlier.FirstOrDefault(s => Names.Equal(s.Source.Alias, pieces[0]));
            if (previous.Table is null) continue;
            var firstField = previous.Table.Field(pieces[1]); var secondField = right.Field(b.Name.Split('.').Last());
            if (ComparisonFamily(firstField.Type) == ComparisonFamily(secondField.Type)) keys.Add((a, b));
        }
        var candidates = SourceRows(right, join.Source.Alias, parameters, token, statistics).ToArray();
        Dictionary<string, List<EvaluationContext>>? index = null;
        string? Key(EvaluationContext row, bool rightSide)
        {
            var values = new object?[keys.Count];
            for (var i = 0; i < keys.Count; i++) { var value = (rightSide ? keys[i].Right : keys[i].Left).Eval(row); if (value is null) return null; values[i] = value; }
            return SqlValue.Key(values);
        }
        if (keys.Count > 0)
        {
            statistics.HashJoins++; index = new(StringComparer.OrdinalIgnoreCase);
            foreach (var row in candidates)
            {
                token.ThrowIfCancellationRequested(); var key = Key(row, true); if (key is null) continue;
                if (!index.TryGetValue(key, out var bucket)) index[key] = bucket = [];
                bucket.Add(row);
            }
        }
        else statistics.NestedLoopJoins++;
        var nullRow = AddSource(new EvaluationContext { Parameters = parameters }, right, join.Source.Alias, null);
        long comparisons = 0; var produced = 0;
        foreach (var row in left)
        {
            token.ThrowIfCancellationRequested(); var matched = false;
            IEnumerable<EvaluationContext> matches = candidates;
            if (index is not null) matches = Key(row, false) is { } key && index.TryGetValue(key, out var bucket) ? bucket : [];
            foreach (var candidate in matches)
            {
                token.ThrowIfCancellationRequested(); statistics.JoinComparisons++;
                if (++comparisons > (long)Options.MaximumIntermediateRows * 16) throw new DataSpaceException("Join work limit exceeded. Narrow the source tables.");
                var merged = MergeSource(row, candidate, right, join.Source.Alias);
                if (join.Condition is null || SqlValue.Truth(join.Condition.Eval(merged)))
                { matched = true; CheckSize(++produced); yield return merged; }
            }
            if (!matched && join.Kind == "LEFT") { CheckSize(++produced); yield return MergeSource(row, nullRow, right, join.Source.Alias); }
        }
    }
    private static int ComparisonFamily(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.AutoNumber or FieldType.Decimal or FieldType.Currency or FieldType.YesNo => 0,
        FieldType.ShortText or FieldType.LongText => 1, FieldType.DateTime => 2, _ => 3
    };
    private static EvaluationContext MergeSource(EvaluationContext left, EvaluationContext right, TableDefinition table, string alias)
    {
        var context = left.Clone();
        foreach (var field in table.Fields)
        {
            var qualified = alias + "." + field.Name; var value = right.Values[qualified]; context.Values[qualified] = value;
            if (context.Values.ContainsKey(field.Name)) context.Ambiguous.Add(field.Name); else context.Values[field.Name] = value;
        }
        return context;
    }
    private static List<string> OutputNames(List<Projection> projections)
    {
        var names = new List<string>();
        for (var i = 0; i < projections.Count; i++)
        {
            var projection = projections[i]; var name = projection.Alias ?? (projection.Expression is NameExpr field ? field.Name.Split('.').Last() : $"Expr{i + 1}");
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (projection.Alias is not null) throw new DataSpaceException($"Duplicate result alias '{name}'.");
                var stem = name; var suffix = 2; while (names.Contains(name, StringComparer.OrdinalIgnoreCase)) name = stem + suffix++;
            }
            names.Add(name);
        }
        return names;
    }
    private static List<Projection> Expand(List<Projection> projections, List<(Source Source, TableDefinition Table)> sources)
    {
        var expanded = new List<Projection>();
        foreach (var projection in projections)
        {
            if (projection.Wildcard is null) { expanded.Add(projection); continue; }
            var matches = sources.Where(s => projection.Wildcard.Length == 0 || Names.Equal(projection.Wildcard, s.Source.Alias)).ToArray();
            if (matches.Length == 0) throw new DataSpaceException("Wildcard projection requires a matching source table.");
            foreach (var (source, table) in matches) foreach (var field in table.Fields) expanded.Add(new(new NameExpr(source.Alias + "." + field.Name)));
        }
        return expanded;
    }
    private static FieldType InferType(object? value, Projection projection, List<(Source Source, TableDefinition Table)> sources)
    {
        if (projection.Expression is NameExpr name)
        {
            var parts = name.Name.Split('.');
            foreach (var (source, table) in sources)
            {
                if (parts.Length == 2 && !Names.Equal(parts[0], source.Alias)) continue;
                var field = table.Fields.FirstOrDefault(f => Names.Equal(f.Name, parts[^1]));
                if (field is not null) return field.Type == FieldType.AutoNumber ? FieldType.Integer : field.Type;
            }
        }
        if (projection.Expression is FunctionExpr function)
        {
            if (function.Name == "COUNT") return FieldType.Integer;
            if (function.Name is "SUM" or "AVG") return FieldType.Decimal;
            if (function.Name is "MIN" or "MAX" or "FIRST" or "LAST" && function.Arguments.Count == 1)
                return InferType(value, new(function.Arguments[0]), sources);
        }
        return value switch { long or int => FieldType.Integer, decimal or double => FieldType.Decimal, DateTime => FieldType.DateTime, bool => FieldType.YesNo, Guid => FieldType.Guid, _ => FieldType.LongText };
    }
}
