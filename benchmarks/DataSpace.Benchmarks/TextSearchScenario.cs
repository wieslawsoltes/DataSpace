using System.Diagnostics;
using DataSpace.Core;

/// <summary>Compare field-scoped searches over the same already-loaded table.</summary>
public static class TextSearchScenario
{
    public static object Measure()
    {
        var fields = Enumerable.Range(0, 64).Select(index => new FieldDefinition { Name = "F" + index }).ToArray();
        var rows = Enumerable.Range(0, 10000).Select(index => new Record { Values = new() { ["F63"] = "Row " + index } }).ToArray();
        var options = new TableSearchOptions("not-present"); const string scope = "F63";
        // Reference is the previous flattened-cell traversal, with the same literal
        // comparison, source data and cancellation check. Both must scan to the end.
        TableSearchHit? Reference()
        {
            IReadOnlyList<FieldDefinition> columns = fields; IReadOnlyList<Record> data = rows;
            var total = (long)columns.Count * data.Count;
            for (long step = 1; step <= total; step++)
            {
                CancellationToken.None.ThrowIfCancellationRequested();
                var at = (-1 + step + total) % total;
                var row = (int)(at / columns.Count); var column = (int)(at % columns.Count); var field = columns[column];
                if (!Names.Equal(field.Name, scope)) continue;
                var record = data[row]; var value = record[field.Name];
                if (value?.Contains(options.Text, StringComparison.OrdinalIgnoreCase) == true) return new(row, column, record.Id, field.Name);
            }
            return null;
        }
        TableSearchHit? Optimized() => TableTextSearch.FindNext(fields, rows, options, onlyField: scope);
        Sample Run(Func<TableSearchHit?> action)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            var result = action();
            var sample = new Sample(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
            if (result is not null) throw new Exception("Field search result mismatch.");
            return sample;
        }
        for (var i = 0; i < 2; i++) { Run(Reference); Run(Optimized); }
        var baseline = new List<Sample>(); var optimized = new List<Sample>();
        for (var i = 0; i < 5; i++)
            if (i % 2 == 0) { baseline.Add(Run(Reference)); optimized.Add(Run(Optimized)); }
            else { optimized.Add(Run(Optimized)); baseline.Add(Run(Reference)); }
        object Summary(List<Sample> samples) => new { medianMilliseconds = samples.Select(s => s.Milliseconds).Order().ElementAt(2), medianAllocatedBytes = samples.Select(s => s.AllocatedBytes).Order().ElementAt(2), samples };
        return new { rows = rows.Length, fields = fields.Length, baseline = Summary(baseline), optimized = Summary(optimized),
            scope = "Managed field-scoped no-match search only. Reference visits every displayed cell position before scope filtering; optimized resolves the column once then visits rows. Both read the same 10,000 stored values; parsing, rendering, persistence and source setup excluded. Not an indexed database or asynchronous browser search." };
    }
}
