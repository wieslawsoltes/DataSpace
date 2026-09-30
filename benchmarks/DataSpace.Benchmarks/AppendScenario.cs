using System.Diagnostics;
using DataSpace.Core;

/// <summary>Compare append transactions, excluding fixture/workspace setup from measured work.</summary>
public static class AppendScenario
{
    public static object Measure()
    {
        var fixture = new DatabaseDocument(); var table = ObjectFactory.CreateTable(fixture);
        for (var index = 0; index < 25000; index++) RecordOperations.Insert(table, new Dictionary<string, string?> { ["Title"] = "Existing " + index });
        var rows = Enumerable.Range(0, 500).Select(index => new string?[] { "Appended " + index }).ToArray();
        var baseline = new List<Sample>(); var optimized = new List<Sample>();
        Sample Run(bool fast)
        {
            var workspace = new DatabaseWorkspace(fixture) { HistoryLimit = 0 };
            var before = workspace.Document; var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            if (fast) workspace.AppendRecords("append", table.Name, ["Title"], rows);
            else workspace.Edit("append", document => { foreach (var row in rows) RecordOperations.Insert(document.Table(table.Name), new Dictionary<string, string?> { ["Title"] = row[0] }); });
            var result = new Sample(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
            var changed = workspace.Document.Table(table.Name);
            if (changed.Records.Count != 25500 || changed.NextAutoNumber != 25501 || before.Tables[0].Records.Count != 25000) throw new Exception("Append transaction shape differs.");
            for (var index = 0; index < changed.Records.Count; index++)
                if (changed.Records[index]["ID"] != (index + 1).ToString() || changed.Records[index]["Title"] != (index < 25000 ? "Existing " + index : "Appended " + (index - 25000))) throw new Exception("Append result values differ.");
            if (fast && !ReferenceEquals(before.Tables[0].Records[0], changed.Records[0])) throw new Exception("Old record was copied.");
            return result;
        }
        for (var index = 0; index < 2; index++) { Run(false); Run(true); }
        for (var index = 0; index < 5; index++)
            if (index % 2 == 0) { baseline.Add(Run(false)); optimized.Add(Run(true)); }
            else { optimized.Add(Run(true)); baseline.Add(Run(false)); }
        object Summary(List<Sample> samples) => new { medianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(2), medianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(2), samples };
        return new { existingRows = 25000, appendedRows = 500, baseline = Summary(baseline), optimized = Summary(optimized), scope = "Managed append transaction only; fixture/workspace construction, persistence, rendering and undo excluded. Baseline deep-copies and validates the entire document; optimized shares old row dictionaries and checks destination constraints. Values/order/next-key match; new internal identities differ. Both copy record-list containers and scan applicable constraints." };
    }
}
