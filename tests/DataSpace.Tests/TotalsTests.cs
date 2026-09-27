using DataSpace.Core;
using DataSpace.Query;
using Xunit;
namespace DataSpace.Tests;
public sealed class TotalsTests
{
    [Fact]
    public void TotalsAreComputedOncePerSnapshotAndInvalidatedExplicitly()
    {
        var document = SampleDatabase.Create(); var view = TableView.Open(document, "Orders"); var cache = new TableTotalsCache();
        var first = cache.GetValues(view.Fields, view); var count = view.MaterializedRecordCount;
        for (var i = 0; i < 50; i++) Assert.Same(first, cache.GetValues(view.Fields, view));
        Assert.Equal(count, view.MaterializedRecordCount); Assert.Equal(1, cache.ComputationCount);
        cache.Invalidate(); cache.GetValues(view.Fields, view); Assert.Equal(2, cache.ComputationCount);
        var field = view.Fields.Select((f, i) => (f, i)).First(p => p.f.Name == "Amount").i;
        Assert.Equal(document.Table("Orders").Records.Sum(r => decimal.Parse(r["Amount"]!, FieldValues.Culture)).ToString("N2", FieldValues.Culture), first[field]);
    }
    [Fact]
    public void IdentityLookupOnVirtualViewsDoesNotMaterializeTheView()
    {
        var document = SampleDatabase.Create(); var view = TableView.Open(document, "Customers");
        Assert.Equal(17, RecordIdentity.IndexOf(view, document.Table("Customers").Records[17].Id));
        Assert.Equal(0, view.MaterializedRecordCount); Assert.Equal(-1, RecordIdentity.IndexOf(view, "missing"));
    }
}
