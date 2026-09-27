using System.Diagnostics;
using DataSpace.Core;

namespace DataSpace.Query;

public sealed class QueryResult
{
    public List<FieldDefinition> Fields { get; init; } = [];
    public List<Record> Records { get; init; } = [];
    public bool IsAction { get; init; }
    public int AffectedRecords { get; init; }
    public TimeSpan Duration { get; internal set; }
    public static QueryResult FromTable(TableDefinition table) => new() { Fields = table.Fields, Records = table.Records.ToList() };
}
public sealed class QueryOptions
{
    public int MaximumIntermediateRows { get; init; } = 250000;
    public int MaximumResultRows { get; init; } = 100000;
}

/// <summary>Single-statement, managed relational executor. Action queries commit atomically through a workspace.</summary>
public sealed class QueryEngine
{
    private readonly Dictionary<string, Statement> _cache = new(StringComparer.Ordinal);
    public QueryOptions Options { get; }
    public QueryEngine(QueryOptions? options = null) { Options = options ?? new(); }
    public bool IsReadOnly(string sql) => Parse(sql) is SelectStatement;
    private Statement Parse(string sql)
    {
        if (_cache.TryGetValue(sql, out var plan)) return plan;
        plan = new SqlParser(sql).Parse();
        if (_cache.Count >= 128) _cache.Clear();
        _cache[sql] = plan; return plan;
    }
    public QueryResult Select(DatabaseDocument document, string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        var plan = Parse(sql) as SelectStatement ?? throw new DataSpaceException("A record source must be a SELECT statement.");
        var timer = Stopwatch.StartNew();
        try
        {
            var result = ExecuteSelect(document, plan, Parameters(parameters), cancellationToken);
            result.Duration = timer.Elapsed; return result;
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentOutOfRangeException)
        { throw new DataSpaceException("Expression evaluation failed: " + error.Message); }
    }
    public QueryResult Execute(DatabaseWorkspace workspace, string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        var plan = Parse(sql);
        if (plan is SelectStatement) return Select(workspace.Document, sql, parameters, cancellationToken);
        var timer = Stopwatch.StartNew(); var affected = 0;
        var args = Parameters(parameters);
        workspace.Edit("Run action query", document =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (plan)
            {
                case InsertStatement insert:
                {
                    var table = document.Table(insert.Table);
                    var fields = insert.Fields.Count == 0 ? table.Fields.Select(f => f.Name).ToList() : insert.Fields;
                    if (fields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Count) throw new DataSpaceException("INSERT contains duplicate field names.");
                    foreach (var values in insert.Rows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (values.Count != fields.Count) throw new DataSpaceException("INSERT field and value counts differ.");
                        var context = new EvaluationContext { Parameters = args };
                        RecordOperations.Insert(table, fields.Select((f, i) => KeyValuePair.Create(f, FieldValues.FromObject(values[i].Eval(context)))).ToDictionary(p => p.Key, p => p.Value));
                        affected++;
                    }
                    break;
                }
                case UpdateStatement update:
                {
                    var table = document.Table(update.Table);
                    if (update.Assignments.Select(a => a.Field).Distinct(StringComparer.OrdinalIgnoreCase).Count() != update.Assignments.Count) throw new DataSpaceException("UPDATE contains duplicate assignments.");
                    var changes = new List<(string Id, Dictionary<string, string?> Values)>();
                    foreach (var row in table.Records)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var context = AddSource(new EvaluationContext { Parameters = args }, table, table.Name, row);
                        if (update.Where is not null && !SqlValue.Truth(update.Where.Eval(context))) continue;
                        changes.Add((row.Id, update.Assignments.ToDictionary(a => table.Field(a.Field).Name, a => FieldValues.FromObject(a.Value.Eval(context)))));
                    }
                    foreach (var change in changes) RecordOperations.Update(document, table.Name, change.Id, change.Values);
                    affected = changes.Count; break;
                }
                case DeleteStatement delete:
                {
                    var table = document.Table(delete.Table);
                    var ids = table.Records.Where(r => delete.Where is null || SqlValue.Truth(delete.Where.Eval(AddSource(new EvaluationContext { Parameters = args }, table, table.Name, r)))).Select(r => r.Id).ToArray();
                    RecordOperations.Delete(document, table.Name, ids); affected = ids.Length; break;
                }
                case CreateStatement create:
                    // Clone the schema because parsed plans are cached and must remain immutable during execution.
                    document.Tables.Add(new TableDefinition { Name = create.Table.Name, Fields = create.Table.Fields.Select(CopyField).ToList() }); break;
                case AlterStatement alter:
                {
                    var table = document.Table(alter.Table);
                    if (alter.Add is { } add) RecordOperations.AddField(table, CopyField(add));
                    else
                    {
                        var field = table.Field(alter.Drop!); table.Fields.Remove(field);
                        foreach (var row in table.Records) row.Values.Remove(field.Name);
                    }
                    break;
                }
                case DropStatement drop:
                    document.Tables.Remove(document.Table(drop.Table)); break;
                default: throw new DataSpaceException("Unsupported statement.");
            }
            cancellationToken.ThrowIfCancellationRequested();
        });
        return new QueryResult { IsAction = true, AffectedRecords = affected, Duration = timer.Elapsed };
    }
    private static Dictionary<string, object?> Parameters(IReadOnlyDictionary<string, object?>? args)
        => args is null ? new(StringComparer.OrdinalIgnoreCase) : new(args, StringComparer.OrdinalIgnoreCase);

    private QueryResult ExecuteSelect(DatabaseDocument document, SelectStatement plan, IReadOnlyDictionary<string, object?> parameters, CancellationToken token)
    {
        var sources = new List<(Source Source, TableDefinition Table)>();
        if (plan.Source is { } from) sources.Add((from, document.Table(from.Table)));
        foreach (var join in plan.Joins) sources.Add((join.Source, document.Table(join.Source.Table)));
        if (sources.Select(s => s.Source.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count) throw new DataSpaceException("Duplicate table alias.");
        var projections = Expand(plan.Projections, sources);
        var rows = plan.Source is null ? new List<EvaluationContext> { new() { Parameters = parameters } }
            : sources[0].Table.Records.Select(r => AddSource(new EvaluationContext { Parameters = parameters }, sources[0].Table, sources[0].Source.Alias, r)).ToList();
        CheckSize(rows.Count);
        for (var i = 0; i < plan.Joins.Count; i++)
        {
            var join = plan.Joins[i]; var table = sources[i + 1].Table;
            var next = new List<EvaluationContext>();
            long comparisons = 0;
            foreach (var row in rows)
            {
                var matched = false;
                foreach (var candidate in table.Records)
                {
                    if ((++comparisons & 255) == 0) token.ThrowIfCancellationRequested();
                    if (comparisons > (long)Options.MaximumIntermediateRows * 16) throw new DataSpaceException("Join work limit exceeded. Narrow the source tables.");
                    var context = AddSource(row, table, join.Source.Alias, candidate);
                    if (join.Condition is null || SqlValue.Truth(join.Condition.Eval(context))) { matched = true; next.Add(context); CheckSize(next.Count); }
                }
                if (!matched && join.Kind == "LEFT") { next.Add(AddSource(row, table, join.Source.Alias, null)); CheckSize(next.Count); }
            }
            rows = next;
        }
        if (plan.Where is { } where)
        {
            if (where.Aggregate) throw new DataSpaceException("Aggregates belong in HAVING, not WHERE.");
            rows = rows.Where(r => { token.ThrowIfCancellationRequested(); return SqlValue.Truth(where.Eval(r)); }).ToList();
        }
        var grouped = plan.Groups.Count != 0 || projections.Any(p => p.Expression.Aggregate) || plan.Having?.Aggregate == true;
        if (plan.Groups.Any(g => g.Aggregate)) throw new DataSpaceException("GROUP BY cannot contain aggregates.");
        if (grouped)
        {
            if (projections.Any(p => !p.Expression.GroupSafe(plan.Groups))) throw new DataSpaceException("Every selected field must be grouped or aggregated.");
            if (plan.Groups.Count == 0)
            {
                var context = rows.FirstOrDefault()?.Clone() ?? new EvaluationContext { Parameters = parameters };
                context.Group = rows; rows = [context];
            }
            else rows = rows.GroupBy(r => SqlValue.Key(plan.Groups.Select(g => g.Eval(r))))
                .Select(group => { var context = group.First().Clone(); context.Group = group.ToList(); return context; }).ToList();
        }
        else if (plan.Having is not null) throw new DataSpaceException("HAVING requires grouping or aggregates.");
        var names = new List<string>();
        for (var i = 0; i < projections.Count; i++)
        {
            var projection = projections[i];
            var name = projection.Alias ?? (projection.Expression is NameExpr field ? field.Name.Split('.').Last() : $"Expr{i + 1}");
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (projection.Alias is not null) throw new DataSpaceException($"Duplicate result alias '{name}'.");
                var stem = name; var suffix = 2; while (names.Contains(name, StringComparer.OrdinalIgnoreCase)) name = stem + suffix++;
            }
            names.Add(name);
        }
        var resultRows = new List<(object?[] Values, object?[] Order)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            var values = projections.Select(p => p.Expression.Eval(row)).ToArray();
            var context = row.Clone();
            for (var i = 0; i < names.Count; i++) { context.Values[names[i]] = values[i]; context.Ambiguous.Remove(names[i]); }
            if (plan.Having is { } having && !SqlValue.Truth(having.Eval(context))) continue;
            if (plan.Distinct && !seen.Add(SqlValue.Key(values))) continue;
            var orderValues = plan.Order.Select(o => o.Expression is LiteralExpr { Value: decimal n } && n == decimal.Truncate(n) && n > 0 && n <= values.Length
                ? values[(int)n - 1] : o.Expression.Eval(context)).ToArray();
            resultRows.Add((values, orderValues));
        }
        if (plan.Order.Count != 0) resultRows.Sort((a, b) =>
        {
            for (var i = 0; i < plan.Order.Count; i++) { var compare = SqlValue.Compare(a.Order[i], b.Order[i]); if (compare != 0) return plan.Order[i].Descending ? -Math.Sign(compare) : compare; }
            return 0;
        });
        var selected = resultRows.Skip(plan.Offset).Take(plan.Limit ?? int.MaxValue).ToList();
        if (selected.Count > Options.MaximumResultRows) throw new DataSpaceException($"The result exceeds {Options.MaximumResultRows:N0} records. Use TOP or LIMIT.");
        var fields = names.Select((name, i) => new FieldDefinition
        {
            Name = name, Width = name.Length > 16 ? 220 : 160,
            Type = InferType(selected.Select(r => r.Values[i]).FirstOrDefault(v => v is not null), projections[i], sources)
        }).ToList();
        return new QueryResult
        {
            Fields = fields,
            Records = selected.Select(row => new Record { Values = names.Select((name, i) => KeyValuePair.Create(name, FieldValues.FromObject(row.Values[i]))).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase) }).ToList()
        };
    }
    private void CheckSize(int count) { if (count > Options.MaximumIntermediateRows) throw new DataSpaceException("Query intermediate-row limit exceeded."); }
    private static FieldDefinition CopyField(FieldDefinition f) => new() { Name = f.Name, Type = f.Type, Required = f.Required, PrimaryKey = f.PrimaryKey, Unique = f.Unique, MaxLength = f.MaxLength, DefaultValue = f.DefaultValue, Width = f.Width };
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
        return value switch { long or int => FieldType.Integer, decimal or double => FieldType.Decimal, DateTime => FieldType.DateTime, bool => FieldType.YesNo, Guid => FieldType.Guid, _ => FieldType.LongText };
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
    private static EvaluationContext AddSource(EvaluationContext original, TableDefinition table, string alias, Record? record)
    {
        var context = original.Clone();
        foreach (var field in table.Fields)
        {
            var value = record is null ? null : FieldValues.Parse(field, record[field.Name]);
            context.Values[alias + "." + field.Name] = value;
            if (context.Values.ContainsKey(field.Name)) context.Ambiguous.Add(field.Name);
            else context.Values[field.Name] = value;
        }
        return context;
    }
}
