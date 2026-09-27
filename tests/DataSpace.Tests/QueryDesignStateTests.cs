using DataSpace.Core;
using DataSpace.Query;
using Xunit;
namespace DataSpace.Tests;
public sealed class QueryDesignStateTests
{
    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData(" \n\t")]
    public void NativeEditorWhitespaceKeepsGridCriteriaAndPositions(string newline)
    {
        var document = SampleDatabase.Create(); var design = QueryDesign.FromSql(document.Queries[0].Sql);
        design.Columns[0].Criteria[0] = "Like 'B*'"; design.Sources[0].X = 380;
        var state = design.Serialize(); var sql = design.ToSql().Replace("\n", newline);
        var restored = QueryDesign.Restore(sql, state);
        Assert.Equal(380, restored.Sources[0].X); Assert.Equal("Like 'B*'", restored.Columns[0].Criteria[0]);
        restored.Columns[0].Criteria[1] = "Like 'T*'";
        Assert.Equal(3, new QueryEngine().Select(document, restored.ToSql()).Records.Count);
    }
    [Fact]
    public void WhitespaceInsideLiteralsIsNeverIgnored()
    {
        var design = QueryDesign.FromSql("SELECT Company FROM Customers WHERE Company='Line\nBreak'"); design.Sources[0].X = 380;
        var changed = design.ToSql().Replace("Line\nBreak", "Line\rBreak");
        var restored = QueryDesign.Restore(changed, design.Serialize());
        Assert.Equal(24, restored.Sources[0].X); Assert.Contains("Line\rBreak", restored.Where);
    }
}
