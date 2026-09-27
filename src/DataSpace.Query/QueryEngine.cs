using System.Diagnostics;
using DataSpace.Core;

namespace DataSpace.Query;

public sealed class QueryStatistics
{
    public long SourceContextsCreated { get; internal set; }
    public long SourceRowsRead { get; internal set; }
    public long JoinComparisons { get; internal set; }
    public int HashJoins { get; internal set; }
    public int NestedLoopJoins { get; internal set; }
    public long AggregateInputRows { get; internal set; }
    public int PeakAggregateGroups { get; internal set; }
    public int BufferedAggregateRows { get; internal set; }
    public long SortCandidateRows { get; internal set; }
    public int PeakSortRows { get; internal set; }
    public long CrosstabCells { get; internal set; }
    public long OutputRows { get; internal set; }
}
public sealed class QueryResult
{
    public List<FieldDefinition> Fields { get; init; } = [];
    public List<Record> Records { get; init; } = [];
    public bool IsAction { get; init; }
    public int AffectedRecords { get; init; }
    public TimeSpan Duration { get; internal set; }
    public QueryStatistics Statistics { get; internal set; } = new();
    public static QueryResult FromTable(TableDefinition table) => new() { Fields = table.Fields.Select(TableSchemaDraft.Copy).ToList(), Records = table.Records.Select(DocumentSnapshot.CopyRecord).ToList() };
}
public sealed class QueryOptions
{
    public int MaximumIntermediateRows { get; init; } = 250000;
    public int MaximumResultRows { get; init; } = 100000;
    public int MaximumSourceDepth { get; init; } = 32;
    public bool EnableReusableRowContexts { get; init; } = true;
    public bool EnableStreamingAggregates { get; init; } = true;
    public bool EnableTopKSort { get; init; } = true;
    public int MaximumCrosstabColumns { get; init; } = 256;
    public int MaximumCrosstabCells { get; init; } = 250000;
    public bool EnableHashJoins { get; init; } = true;
}

/// <summary>Managed relational executor with bounded plans, streaming projections and atomic action queries.</summary>
public sealed partial class QueryEngine
{
    private readonly Dictionary<string, Statement> _cache = new(StringComparer.Ordinal);
    private readonly object _cacheLock = new();
    public QueryOptions Options { get; }
    public QueryEngine(QueryOptions? options = null)
    {
        Options = options ?? new();
        if (Options.MaximumIntermediateRows < 1 || Options.MaximumResultRows < 1 || Options.MaximumSourceDepth is < 1 or > 64 || Options.MaximumCrosstabColumns is < 1 or > 256 || Options.MaximumCrosstabCells < 1) throw new ArgumentOutOfRangeException(nameof(options));
    }
    public bool IsReadOnly(string sql) => Parse(sql) is SelectStatement or UnionStatement or TransformStatement;
    private Statement Parse(string sql)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(sql, out var plan)) return plan;
            plan = new SqlParser(sql).Parse(); if (_cache.Count >= 128) _cache.Clear(); _cache[sql] = plan; return plan;
        }
    }
    public QueryResult Select(DatabaseDocument document, string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var timer = Stopwatch.StartNew(); var statistics = new QueryStatistics();
        try
        {
            var result = Read(document, Parse(sql), Parameters(parameters), cancellationToken, new(StringComparer.OrdinalIgnoreCase), statistics);
            statistics.OutputRows = result.Records.Count; result.Statistics = statistics; result.Duration = timer.Elapsed; return result;
        }
        catch (InvalidOperationException error) when (error.InnerException is OperationCanceledException)
        { throw new OperationCanceledException(cancellationToken); }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentOutOfRangeException)
        { throw new DataSpaceException("Expression evaluation failed: " + error.Message); }
    }
    public QueryResult Execute(DatabaseWorkspace workspace, string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var plan = Parse(sql);
        if (plan is SelectStatement or UnionStatement or TransformStatement) return Select(workspace.Document, sql, parameters, cancellationToken);
        var timer = Stopwatch.StartNew(); var affected = 0; var args = Parameters(parameters);
        workspace.Edit("Run action query", document =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (plan)
            {
                case InsertStatement insert:
                {
                    var table = document.Table(insert.Table); var fields = InsertFields(table, insert.Fields);
                    foreach (var values in insert.Rows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (values.Count != fields.Count) throw new DataSpaceException("INSERT field and value counts differ.");
                        var context = new EvaluationContext { Parameters = args };
                        RecordOperations.Insert(table, fields.Select((f, i) => KeyValuePair.Create(f, FieldValues.FromObject(values[i].Eval(context)))).ToDictionary(p => p.Key, p => p.Value)); affected++;
                    }
                    break;
                }
                case InsertSelectStatement insert:
                {
                    var table = document.Table(insert.Table); var fields = InsertFields(table, insert.Fields);
                    // Materialize the source before inserting, including self-appends.
                    var result = Read(document, insert.Query, args, cancellationToken, new(StringComparer.OrdinalIgnoreCase), new());
                    if (fields.Count != result.Fields.Count) throw new DataSpaceException("INSERT field and SELECT column counts differ.");
                    foreach (var row in result.Records)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        RecordOperations.Insert(table, fields.Select((f, i) => KeyValuePair.Create(f, row[result.Fields[i].Name])).ToDictionary(p => p.Key, p => p.Value)); affected++;
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
                        cancellationToken.ThrowIfCancellationRequested(); var context = AddSource(new EvaluationContext { Parameters = args }, table, table.Name, row);
                        if (update.Where is not null && !SqlValue.Truth(update.Where.Eval(context))) continue;
                        changes.Add((row.Id, update.Assignments.ToDictionary(a => table.Field(a.Field).Name, a => FieldValues.FromObject(a.Value.Eval(context)))));
                    }
                    foreach (var change in changes) { cancellationToken.ThrowIfCancellationRequested(); RecordOperations.Update(document, table.Name, change.Id, change.Values); }
                    affected = changes.Count; break;
                }
                case DeleteStatement delete:
                {
                    var table = document.Table(delete.Table); var ids = new List<string>();
                    foreach (var row in table.Records)
                    { cancellationToken.ThrowIfCancellationRequested(); if (delete.Where is null || SqlValue.Truth(delete.Where.Eval(AddSource(new EvaluationContext { Parameters = args }, table, table.Name, row)))) ids.Add(row.Id); }
                    RecordOperations.Delete(document, table.Name, ids); affected = ids.Count; break;
                }
                case MakeTableStatement make: affected = MakeTable(document, make, args, cancellationToken); break;
                case CreateIndexStatement createIndex:
                    document.Table(createIndex.Table).Indexes.Add(new() { Name = createIndex.Index.Name, Fields = createIndex.Index.Fields.ToList(), Unique = createIndex.Index.Unique }); break;
                case DropIndexStatement dropIndex:
                {
                    var table = document.Table(dropIndex.Table);
                    var index = table.Indexes.FirstOrDefault(i => Names.Equal(i.Name, dropIndex.Index)) ?? throw new DataSpaceException("Index does not exist: " + dropIndex.Index);
                    table.Indexes.Remove(index); break;
                }
                case CreateStatement create:
                    document.Tables.Add(new TableDefinition { Name = create.Table.Name, Fields = create.Table.Fields.Select(TableSchemaDraft.Copy).ToList() }); break;
                case AlterStatement alter:
                {
                    var table = document.Table(alter.Table);
                    if (alter.Add is { } add) RecordOperations.AddField(table, TableSchemaDraft.Copy(add));
                    else { var field = table.Field(alter.Drop!); table.Fields.Remove(field); foreach (var row in table.Records) row.Values.Remove(field.Name); }
                    break;
                }
                case DropStatement drop: document.Tables.Remove(document.Table(drop.Table)); break;
                default: throw new DataSpaceException("Unsupported statement.");
            }
            cancellationToken.ThrowIfCancellationRequested();
        });
        return new QueryResult { IsAction = true, AffectedRecords = affected, Duration = timer.Elapsed };
    }
    private static List<string> InsertFields(TableDefinition table, List<string> requested)
    {
        var fields = requested.Count == 0 ? table.Fields.Select(f => f.Name).ToList() : requested;
        if (fields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Count) throw new DataSpaceException("INSERT contains duplicate field names.");
        foreach (var field in fields) table.Field(field); return fields;
    }
    private static Dictionary<string, object?> Parameters(IReadOnlyDictionary<string, object?>? args) => args is null ? new(StringComparer.OrdinalIgnoreCase) : new(args, StringComparer.OrdinalIgnoreCase);
    private void CheckSize(int count) { if (count > Options.MaximumIntermediateRows) throw new DataSpaceException("Query intermediate-row limit exceeded."); }
    private static EvaluationContext AddSource(EvaluationContext original, TableDefinition table, string alias, Record? record)
    {
        var context = original.Clone();
        foreach (var field in table.Fields)
        {
            var value = record is null ? null : FieldValues.Parse(field, record[field.Name]); context.Values[alias + "." + field.Name] = value;
            if (context.Values.ContainsKey(field.Name)) context.Ambiguous.Add(field.Name); else context.Values[field.Name] = value;
        }
        return context;
    }
}
