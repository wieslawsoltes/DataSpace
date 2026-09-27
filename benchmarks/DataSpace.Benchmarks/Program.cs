using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DataSpace.Core;
using DataSpace.Query;

// Diagnostic benchmark, not an absolute-time CI gate. Same fixtures, Release process,
// warmup and alternating measurements. Reports medians and per-thread allocated bytes.
var path = args.Length > 0 ? args[0] : "artifacts/performance.json";
var fixture = new DatabaseDocument(); var table = ObjectFactory.CreateTable(fixture);
for (var i = 0; i < 25000; i++) RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Record " + i });
SchemaValidator.Validate(fixture);
var workspace = new DatabaseWorkspace(fixture) { HistoryLimit = 0 };
var id = workspace.Document.Tables[0].Records[12500].Id; var change = 0;
var results = new List<object>();
void Pair(string name, string baselineLabel, Func<object> baseline, string optimizedLabel, Func<object> optimized, int repetitions = 5)
{
    for (var i = 0; i < 2; i++) { GC.KeepAlive(baseline()); GC.KeepAlive(optimized()); }
    var before = new List<Sample>(); var after = new List<Sample>();
    Sample Measure(Func<object> action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var allocation = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        var value = action(); var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocation; GC.KeepAlive(value); return new(elapsed, bytes);
    }
    for (var i = 0; i < repetitions; i++)
    {
        if (i % 2 == 0) { before.Add(Measure(baseline)); after.Add(Measure(optimized)); }
        else { after.Add(Measure(optimized)); before.Add(Measure(baseline)); }
    }
    object Summary(string label, List<Sample> samples) => new { label, medianMilliseconds = samples.OrderBy(s => s.Milliseconds).ElementAt(samples.Count / 2).Milliseconds, medianAllocatedBytes = samples.OrderBy(s => s.AllocatedBytes).ElementAt(samples.Count / 2).AllocatedBytes, samples };
    results.Add(new { name, baseline = Summary(baselineLabel, before), optimized = Summary(optimizedLabel, after) });
    Console.WriteLine($"{name}: {before.OrderBy(s => s.Milliseconds).ElementAt(repetitions / 2).Milliseconds:F3} ms -> {after.OrderBy(s => s.Milliseconds).ElementAt(repetitions / 2).Milliseconds:F3} ms");
}
Pair("Single cell edit / 25,000 records", "Original JSON clone, mutation and full validation", () =>
{
    var draft = DocumentCodec.Clone(workspace.Document);
    RecordOperations.Update(draft, table.Name, id, new Dictionary<string, string?> { ["Title"] = "Edit " + ++change });
    SchemaValidator.Validate(draft); return draft;
}, "Targeted workspace transaction with unchanged-row sharing", () =>
{
    workspace.UpdateRecords("benchmark", table.Name, [new(id, "Title", "Edit " + ++change)]); return workspace.Document;
});
Pair("Open table / 25,000 records / first 50 visible", "Eager detached view", () => TableView.Select(fixture, table.Name),
    "Lazy view and 50 detached visible rows", () => TableView.Open(fixture, table.Name).ReadPage(0, 50));
var joins = new DatabaseDocument(); var left = ObjectFactory.CreateTable(joins); var right = ObjectFactory.CreateTable(joins);
for (var i = 0; i < 1000; i++) { RecordOperations.Insert(left, new Dictionary<string, string?> { ["Title"] = "Left" }); RecordOperations.Insert(right, new Dictionary<string, string?> { ["Title"] = "Right" }); }
SchemaValidator.Validate(joins);
var hash = new QueryEngine(); var nested = new QueryEngine(new() { EnableHashJoins = false });
const string sql = "SELECT a.ID FROM Table1 a INNER JOIN Table2 b ON a.ID=b.ID";
var fast = hash.Select(joins, sql); var slow = nested.Select(joins, sql);
if (!fast.Records.Select(r => r["ID"]).SequenceEqual(slow.Records.Select(r => r["ID"]))) throw new Exception("Differential join mismatch.");
Pair("Equality join / 1,000 by 1,000 records", "Reference nested loop", () => nested.Select(joins, sql), "Hash candidate lookup", () => hash.Select(joins, sql), 3);
var analytics = new DatabaseDocument();
var entries = new TableDefinition { Name = "Entries", Fields = [new() { Name = "ID", Type = FieldType.Integer }, new() { Name = "Category", Type = FieldType.Integer }, new() { Name = "Amount", Type = FieldType.Decimal }] };
for (var i = 0; i < 50000; i++) RecordOperations.Insert(entries, new Dictionary<string, string?> { ["ID"] = i.ToString(), ["Category"] = (i % 32).ToString(), ["Amount"] = ((i * 3571L) % 7919).ToString() });
analytics.Tables.Add(entries); SchemaValidator.Validate(analytics);
var streamed = new QueryEngine();
var buffered = new QueryEngine(new() { EnableStreamingAggregates = false, EnableTopKSort = false, EnableReusableRowContexts = false });
const string aggregateSql = "SELECT Category, Sum(Amount) AS Total, Avg(Amount) AS Mean, Count(*) AS N, Min(Amount) AS Low, Max(Amount) AS High FROM Entries GROUP BY Category ORDER BY Total DESC";
const string topSql = "SELECT TOP 20 ID, Amount FROM Entries ORDER BY Amount DESC, ID";
void SameRows(QueryResult a, QueryResult b)
{
    if (a.Records.Count != b.Records.Count || !a.Fields.Select(f => f.Name).SequenceEqual(b.Fields.Select(f => f.Name))) throw new Exception("Analytics schema/count mismatch.");
    for (var i = 0; i < a.Records.Count; i++)
        if (!a.Fields.Select(f => a.Records[i][f.Name]).SequenceEqual(b.Fields.Select(f => b.Records[i][f.Name]))) throw new Exception("Analytics differential result mismatch.");
}
var aggregateFast = streamed.Select(analytics, aggregateSql); var aggregateSlow = buffered.Select(analytics, aggregateSql); SameRows(aggregateFast, aggregateSlow);
var topFast = streamed.Select(analytics, topSql); var topSlow = buffered.Select(analytics, topSql); SameRows(topFast, topSlow);
Pair("Grouped analytics / 50,000 records / 32 groups", "Buffered contexts and repeated aggregate scans", () => buffered.Select(analytics, aggregateSql), "Reusable scalar context and streaming accumulators", () => streamed.Select(analytics, aggregateSql));
Pair("Ordered TOP 20 / 50,000 records", "Distinct source contexts and full stable sort", () => buffered.Select(analytics, topSql), "Reusable scalar context and bounded stable selection", () => streamed.Select(analytics, topSql));
var output = new
{
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    processorCount = Environment.ProcessorCount, configuration = "Release", measuredAtUtc = DateTime.UtcNow,
    scope = "Managed engine microbenchmarks, not browser or hardware-GPU measurements; no storage I/O. Timings are environment-dependent.",
    aggregation = new { referenceRowsBuffered = aggregateSlow.Statistics.BufferedAggregateRows, optimizedGroupsRetained = aggregateFast.Statistics.PeakAggregateGroups, optimizedRowsBuffered = aggregateFast.Statistics.BufferedAggregateRows },
    orderedTop = new { candidates = topFast.Statistics.SortCandidateRows, referencePeakRows = topSlow.Statistics.PeakSortRows, optimizedPeakRows = topFast.Statistics.PeakSortRows },
    joins = new { rows = fast.Records.Count, hashComparisons = fast.Statistics.JoinComparisons, referenceComparisons = slow.Statistics.JoinComparisons }, results
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
File.WriteAllText(path, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
public sealed record Sample(double Milliseconds, long AllocatedBytes);
