using System.Text.Json;
using DataSpace.Core;
using Xunit;

namespace DataSpace.DataSources.Tests;

public sealed class ExportBoundaryTests
{
    private static readonly FieldDefinition[] Fields = [new() { Name = "Text", Type = FieldType.LongText }];
    private static Record Row(string text) => new() { Values = new() { ["Text"] = text } };

    [Fact]
    public void PendingJsonBytesStopEnumerationBeforeReadingWholeInput()
    {
        var visited = 0;
        IEnumerable<Record> Rows()
        {
            for (var i = 0; i < 10000; i++) { visited++; yield return Row(new string('x', 100)); }
        }
        Assert.Throws<DataSpaceException>(() => SourceImport.ExportJson(Fields, Rows(), CancellationToken.None, maximumBytes: 256));
        Assert.InRange(visited, 1, 3);
    }

    [Theory]
    [InlineData("quotes \" and slash \\")]
    [InlineData("Żółć 日本語 😀")]
    [InlineData("\u0001\u0002\u0003")]
    public void ExactUtf8ByteLimitIncludesEscapesAndFinalArrayToken(string value)
    {
        var rows = new[] { Row(value) }; var expected = SourceImport.ExportJson(Fields, rows);
        Assert.Equal(expected, SourceImport.ExportJson(Fields, rows, CancellationToken.None, expected.Length));
        Assert.Throws<DataSpaceException>(() => SourceImport.ExportJson(Fields, rows, CancellationToken.None, expected.Length - 1));
        using var document = JsonDocument.Parse(expected); Assert.Equal(value, document.RootElement[0].GetProperty("Text").GetString());
    }

    [Fact]
    public void CancellationIsCheckedBeforeEnumeratingAndBetweenRows()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        IEnumerable<Record> FailEnumeration() { throw new Exception("Must not enumerate"); }
        Assert.ThrowsAny<OperationCanceledException>(() => SourceImport.ExportJson(Fields, FailEnumerationDeferred(), cancelled.Token));
        IEnumerable<Record> FailEnumerationDeferred() { foreach (var row in FailEnumeration()) yield return row; }
        using var during = new CancellationTokenSource();
        IEnumerable<Record> Rows() { yield return Row("first"); during.Cancel(); yield return Row("second"); }
        Assert.ThrowsAny<OperationCanceledException>(() => SourceImport.ExportJson(Fields, Rows(), during.Token));
    }

    [Fact]
    public void EmptyOutputRespectsByteLimit() => Assert.Throws<DataSpaceException>(() => SourceImport.ExportJson(Fields, [], CancellationToken.None, 1));

    [Theory]
    [InlineData(-1)][InlineData(0)][InlineData(SourceLimits.MaxFileBytes + 1)]
    public void InvalidByteLimitRejected(int maximum) => Assert.Throws<ArgumentOutOfRangeException>(() => SourceImport.ExportJson(Fields, [], CancellationToken.None, maximum));

    [Fact]
    public void MalformedWireShapesFailWithControlledErrors()
    {
        SourcePage[] pages = [null!, new(null!, [], false), new([], [], false), new([new("x")], null!, false),
            new([new("x")], [null!], false), new([null!], [], false), new([new(null!)], [], false), new([new("x"), new("x")], [], false)];
        foreach (var page in pages) Assert.Throws<DataSpaceException>(() => SourceLimits.Validate(page, 10));
    }
}
