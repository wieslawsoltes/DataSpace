using System.Diagnostics;
using DataSpace.Core;

public static class DatasheetLayoutScenario
{
    public static object Measure()
    {
        var document = new DatabaseDocument(); var table = ObjectFactory.CreateTable(document);
        for (var i = 0; i < 25000; i++) RecordOperations.Insert(table, new Dictionary<string,string?> { ["Title"] = "Row " + i });
        var baseline = new List<Sample>(); var optimized = new List<Sample>();
        Sample Run(bool fast)
        {
            var workspace = new DatabaseWorkspace(document) { HistoryLimit = 0 }; var old = workspace.Document.Tables[0].Records[0];
            var layout = new DatasheetLayout { FontSize = 14, ColumnOrder = ["Title", "ID"], FrozenFields = ["Title"] };
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            if (fast) workspace.ConfigureDatasheet("Table1", layout, "Title", 250);
            else workspace.Edit("layout", draft => { draft.Tables[0].Datasheet = layout.Copy(); draft.Tables[0].Field("Title").Width = 250; });
            var sample = new Sample(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
            var result = workspace.Document.Tables[0];
            if (result.Records.Count != 25000 || result.Datasheet.FontSize != 14 || result.Field("Title").Width != 250 || result.Records[0]["Title"] != "Row 0") throw new Exception("Layout result mismatch.");
            if (fast && !ReferenceEquals(old, result.Records[0])) throw new Exception("Layout copied a record dictionary.");
            return sample;
        }
        for (var i = 0; i < 2; i++) { Run(false); Run(true); }
        for (var i = 0; i < 5; i++) if (i % 2 == 0) { baseline.Add(Run(false)); optimized.Add(Run(true)); } else { optimized.Add(Run(true)); baseline.Add(Run(false)); }
        object Summary(List<Sample> samples) => new { medianMilliseconds = samples.Select(s => s.Milliseconds).Order().ElementAt(2), medianAllocatedBytes = samples.Select(s => s.AllocatedBytes).Order().ElementAt(2), samples };
        return new { rows = 25000, baseline = Summary(baseline), optimized = Summary(optimized), scope = "Managed metadata transaction; generic detached edit/full validation versus layout-only validation and shared records. Fixture/workspace setup, persistence, undo and rendering excluded. Record-list containers are still copied." };
    }
}
