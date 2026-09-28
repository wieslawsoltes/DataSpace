using DataSpace.Core;

namespace DataSpace.Query;

public sealed partial class QueryEngine
{
    private sealed class PivotGroup(AggregateGroup totals)
    {
        public AggregateGroup Totals { get; } = totals;
        public Dictionary<string, AggregateState> Cells { get; } = new(StringComparer.Ordinal);
    }
    private sealed record PivotColumn(string Key, string Name, object Value);
    private QueryResult ExecuteTransform(DatabaseDocument document, TransformStatement transform,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken token, HashSet<string> path, QueryStatistics statistics, QueryExecution execution, EvaluationContext? outer = null)
    {
        var plan = transform.Query;
        var sources = new List<(Source Source, TableDefinition Table)>();
        if (plan.Source is { } from) sources.Add((from, ResolveSource(document, from.Table, parameters, token, path, statistics, execution)));
        foreach (var join in plan.Joins) sources.Add((join.Source, ResolveSource(document, join.Source.Table, parameters, token, path, statistics, execution)));
        if (sources.Select(s => s.Source.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            throw new DataSpaceException("Duplicate table alias.");
        if (plan.Projections.Any(p => p.Wildcard is not null || !p.Expression.GroupSafe(plan.Groups)))
            throw new DataSpaceException("Crosstab row headings must be grouped or aggregated, without wildcards.");
        var scope = new SubqueryScope(this, document, parameters, token, path, statistics, execution, execution.Depth);
        var environment = new EvaluationContext { Parameters = parameters, Outer = outer, Subqueries = scope.Evaluate };
        var schema = environment.Clone();
        foreach (var source in sources) schema = AddSource(schema, source.Table, source.Source.Alias, null);
        void Bind(Expr expression) => scope.Bind(expression, schema);
        void Scalar(Expr expression)
        { if (expression.Aggregate) throw new DataSpaceException("WHERE, GROUP BY, PIVOT and ON require non-aggregate expressions."); Bind(expression); }
        foreach (var projection in plan.Projections) Bind(projection.Expression);
        foreach (var group in plan.Groups) Scalar(group);
        Scalar(transform.Pivot); Bind(transform.Aggregate);
        if (plan.Where is { } predicate) Scalar(predicate);
        foreach (var join in plan.Joins) if (join.Condition is { } on) Scalar(on);
        var names = OutputNames(plan.Projections);
        if (names.Count > Options.MaximumCrosstabColumns) throw new DataSpaceException("Crosstab column limit exceeded.");
        foreach (var name in names) { schema.Values[name] = null; schema.Ambiguous.Remove(name); }
        if (plan.Having is { } having) Bind(having);
        foreach (var order in plan.Order) Bind(order.Expression);
        IEnumerable<Expr> expressions = plan.Projections.Select(p => p.Expression).Concat(plan.Order.Select(o => o.Expression));
        if (plan.Having is not null) expressions = expressions.Append(plan.Having);
        var functions = expressions.SelectMany(ExpressionAnalysis.Aggregates).Distinct().ToArray();
        foreach (var function in functions) AggregateState.Validate(function);
        var columns = new Dictionary<string, PivotColumn>(StringComparer.Ordinal);
        var columnNames = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        void AddColumn(object? value)
        {
            if (value is null) throw new DataSpaceException("Fixed pivot headings cannot be NULL.");
            var key = SqlValue.Key([value]);
            var name = SqlValue.Text(value);
            if (columns.ContainsKey(key) || !columnNames.Add(name)) throw new DataSpaceException("Duplicate or conflicting pivot heading: " + name);
            if (name.Length is < 1 or > 64 || name.Any(char.IsControl)) throw new DataSpaceException("Pivot headings require 1–64 non-control characters.");
            if (columns.Count + names.Count >= Options.MaximumCrosstabColumns) throw new DataSpaceException("Crosstab column limit exceeded.");
            columns.Add(key, new(key, name, value));
        }
        if (transform.Headings is not null)
            foreach (var heading in transform.Headings)
            {
                token.ThrowIfCancellationRequested();
                if (heading.Aggregate) throw new DataSpaceException("Fixed headings must be scalar values or parameters.");
                AddColumn(heading.Eval(new EvaluationContext { Parameters = parameters }));
            }
        var scanExpressions = expressions.Concat(plan.Groups).Append(transform.Pivot).Append(transform.Aggregate);
        if (plan.Where is not null) scanExpressions = scanExpressions.Append(plan.Where);
        var scanFields = plan.Joins.Count == 0 ? PrunedFields(sources[0].Table, sources[0].Source.Alias, scanExpressions.Concat(scope.ScanReferences)) : null;
        IEnumerable<EvaluationContext> rows = SourceRows(sources[0].Table, sources[0].Source.Alias, parameters, token, statistics, Options.EnableReusableRowContexts && plan.Joins.Count == 0, scanFields, environment, execution);
        for (var i = 0; i < plan.Joins.Count; i++) rows = JoinRows(rows, plan.Joins[i], sources[i + 1].Table, sources.Take(i + 1).ToList(), parameters, token, statistics, execution);
        var groups = new Dictionary<string, PivotGroup>(StringComparer.Ordinal); long cells = 0;
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            if (plan.Where is { } where && !SqlValue.Truth(where.Eval(row))) continue;
            statistics.AggregateInputRows++;
            var key = SqlValue.Key(plan.Groups.Select(g => g.Eval(row)));
            if (!groups.TryGetValue(key, out var group))
            {
                CheckSize(groups.Count + 1);
                if ((long)(groups.Count + 1) * Math.Max(names.Count, Math.Max(1, functions.Length)) > Options.MaximumCrosstabCells)
                    throw new DataSpaceException("Crosstab group-state limit exceeded.");
                groups.Add(key, group = new(new(row, functions)));
            }
            // Row totals include all matching source rows, including values excluded by IN.
            group.Totals.Add(row);
            var value = transform.Pivot.Eval(row); if (value is null) continue;
            var pivotKey = SqlValue.Key([value]);
            if (!columns.ContainsKey(pivotKey))
            { if (transform.Headings is not null) continue; AddColumn(value); }
            if (!group.Cells.TryGetValue(pivotKey, out var cell))
            {
                if (++cells > Options.MaximumCrosstabCells) throw new DataSpaceException("Crosstab cell limit exceeded.");
                group.Cells.Add(pivotKey, cell = new(transform.Aggregate));
            }
            cell.Add(row);
        }
        statistics.PeakAggregateGroups = Math.Max(statistics.PeakAggregateGroups, groups.Count);
        statistics.CrosstabCells += cells;
        var headings = transform.Headings is not null ? columns.Values.ToList()
            : columns.Values.OrderBy(c => c.Value, SqlValue.Comparer).ToList();
        var output = new List<SelectedRow>(); var ordinal = 0;
        foreach (var group in groups.Values)
        {
            token.ThrowIfCancellationRequested(); var context = group.Totals.Complete();
            var rowHeadings = plan.Projections.Select(p => p.Expression.Eval(context)).ToArray();
            for (var i = 0; i < names.Count; i++) { context.Values[names[i]] = rowHeadings[i]; context.Ambiguous.Remove(names[i]); }
            if (plan.Having is { } filter && !SqlValue.Truth(filter.Eval(context))) continue;
            if (output.Count >= Options.MaximumResultRows || (long)(output.Count + 1) * (names.Count + headings.Count) > Options.MaximumCrosstabCells)
                throw new DataSpaceException("Crosstab output limit exceeded.");
            var values = rowHeadings.Concat(headings.Select(h => group.Cells.TryGetValue(h.Key, out var cell) ? cell.Value : null)).ToArray();
            var order = plan.Order.Select(o => o.Expression is LiteralExpr { Value: decimal n } && n == decimal.Truncate(n) && n > 0 && n <= rowHeadings.Length
                ? rowHeadings[(int)n - 1] : o.Expression.Eval(context)).ToArray();
            output.Add(new(values, order, ordinal++));
        }
        if (plan.Order.Count > 0) output.Sort(new RowOrdering(plan.Order, token));
        var fields = names.Select((name, i) => ResultField(name, output.Select(r => r.Values[i]).FirstOrDefault(v => v is not null), plan.Projections[i], sources)).ToList();
        foreach (var heading in headings)
        {
            var index = fields.Count;
            fields.Add(ResultField(heading.Name, output.Select(r => r.Values[index]).FirstOrDefault(v => v is not null), new(transform.Aggregate), sources));
        }
        return new()
        {
            Fields = fields,
            Records = output.Select(row => new Record { Values = fields.Select((field, i) => KeyValuePair.Create(field.Name, FieldValues.FromObject(row.Values[i]))).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase) }).ToList()
        };
    }
    private int MakeTable(DatabaseDocument document, MakeTableStatement statement, IReadOnlyDictionary<string, object?> args, CancellationToken token, QueryStatistics statistics, QueryExecution execution)
    {
        Names.Validate(statement.Table);
        if (document.Tables.Any(t => Names.Equal(t.Name, statement.Table)) || document.Queries.Any(q => Names.Equal(q.Name, statement.Table)))
            throw new DataSpaceException("A table or query with that name already exists: " + statement.Table);
        var result = Read(document, statement.Query, args, token, new(StringComparer.OrdinalIgnoreCase), statistics, execution);
        var table = new TableDefinition { Name = statement.Table, Fields = result.Fields.Select(field => new FieldDefinition
        { Name = field.Name, Type = field.Type, MaxLength = field.MaxLength, Width = field.Width }).ToList() };
        foreach (var row in result.Records) { token.ThrowIfCancellationRequested(); RecordOperations.Insert(table, row.Values); }
        document.Tables.Add(table); return table.Records.Count;
    }
}
