using System.Text.Json;
using System.Diagnostics;
using DataSpace.Core;
using DataSpace.DataSources;

/// <summary>Compare full import followed by field selection with selective import for the same output table.</summary>
public sealed class ImportProjectionScenario : IDisposable
{
    private readonly JsonDataSource _source;
    private readonly SourceTable _table;
    private readonly SourceImportPlan _plan;
    private readonly HashSet<string> _included = new(["Field0", "Field7", "Field31"], StringComparer.Ordinal);
    public ImportProjectionScenario()
    {
        var json = JsonSerializer.Serialize(Enumerable.Range(0, 5000).Select(row =>
            Enumerable.Range(0, 32).ToDictionary(column => "Field" + column, column => (string?)(row + ":" + column))));
        _source = new(json); _table = _source.GetTablesAsync().GetAwaiter().GetResult()[0];
        _plan = SourceImportPlan.CreateDefault(_table);
        _plan.Fields = _plan.Fields.Select(field => field with { Include = _included.Contains(field.Name) }).ToList();
        var reference = FullThenSelect(); var projected = SelectiveImport();
        if (reference.Records.Count != projected.Records.Count || !reference.Fields.Select(f => f.Name).SequenceEqual(projected.Fields.Select(f => f.Name)))
            throw new Exception("Import projection shape mismatch.");
        for (var i = 0; i < reference.Records.Count; i++)
            foreach (var field in reference.Fields)
                if (reference.Records[i][field.Name] != projected.Records[i][field.Name]) throw new Exception("Import projection value mismatch.");
    }
    public TableDefinition FullThenSelect()
    {
        var full = SourceImport.ReadTableAsync(_source, _table, "Copy").GetAwaiter().GetResult();
        var skipped = full.Fields.Where(field => !_included.Contains(field.Name)).Select(field => field.Name).ToArray();
        full.Fields.RemoveAll(field => !_included.Contains(field.Name));
        // The reference is already detached: remove discarded fields in place,
        // rather than inflating the baseline with a second row graph/identity set.
        foreach (var row in full.Records)
            foreach (var field in skipped) row.Values.Remove(field);
        SchemaValidator.ValidateTable(full); return full;
    }
    public TableDefinition SelectiveImport() => SourceImport.ReadTableAsync(_source, _table, "Copy", _plan).GetAwaiter().GetResult();
    public static object Measure()
    {
        using var scenario = new ImportProjectionScenario();
        for (var i = 0; i < 2; i++) { scenario.FullThenSelect(); scenario.SelectiveImport(); }
        var baseline = new List<ImportSample>(); var optimized = new List<ImportSample>();
        ImportSample Read(Func<TableDefinition> action)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
            var table = action(); var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated; GC.KeepAlive(table);
            return new(elapsed, bytes);
        }
        for (var i = 0; i < 5; i++)
        {
            if (i % 2 == 0) { baseline.Add(Read(scenario.FullThenSelect)); optimized.Add(Read(scenario.SelectiveImport)); }
            else { optimized.Add(Read(scenario.SelectiveImport)); baseline.Add(Read(scenario.FullThenSelect)); }
        }
        object Summary(List<ImportSample> samples) => new { medianMilliseconds = samples.Select(s => s.Milliseconds).Order().ElementAt(2),
            medianAllocatedBytes = samples.Select(s => s.AllocatedBytes).Order().ElementAt(2), samples };
        return new { rows = 5000, sourceFields = 32, destinationFields = 3,
            baseline = Summary(baseline), optimized = Summary(optimized),
            scope = "Warm parsed JSON. Baseline imports all fields, removes unwanted fields in place and validates three; optimized imports those same three directly. Both fetch all source columns. Output names, values and order match; internal row IDs differ. Source construction, I/O, workspace commit and Uno rendering excluded." };
    }
    private sealed record ImportSample(double Milliseconds, long AllocatedBytes);
    public void Dispose() => _source.DisposeAsync().GetAwaiter().GetResult();
}
