using System.Collections;
using DataSpace.Core;
using Xunit;

namespace DataSpace.Tests;

public sealed class TextSearchBoundaryTests
{
    [Fact]
    public void ScopedFindMatchesIndependentFlattenedReferenceForEveryStartingCell()
    {
        var fields = Enumerable.Range(0, 4).Select(index => new FieldDefinition { Name = "F" + index }).ToArray();
        var rows = Enumerable.Range(0, 5).Select(row => new Record { Values = fields.ToDictionary(field => field.Name,
            field => (string?)(row % 3 == 0 ? "Match" : "different")) }).ToArray();
        foreach (var backwards in new[] { false, true })
        foreach (var name in new[] { "f0", "F2", "F3", "missing" })
        for (var startRow = -1; startRow <= rows.Length; startRow++)
        for (var startColumn = -1; startColumn <= fields.Length; startColumn++)
        {
            var options = new TableSearchOptions("MATCH", TextMatchMode.WholeField, Backwards: backwards);
            var cells = rows.SelectMany((row, r) => fields.Select((field, c) => new TableSearchHit(r, c, row.Id, field.Name))).ToArray();
            var valid = startRow >= 0 && startRow < rows.Length && startColumn >= 0 && startColumn < fields.Length;
            var start = valid ? startRow * fields.Length + startColumn : backwards ? 0 : -1;
            var expected = Enumerable.Range(1, cells.Length).Select(step => cells[(start + (backwards ? -step : step) + cells.Length) % cells.Length])
                .FirstOrDefault(hit => Names.Equal(hit.Field, name) && rows[hit.Row][hit.Field]!.Equals("MATCH", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expected, TableTextSearch.FindNext(fields, rows, options, startRow, startColumn, name));
        }
    }
    [Fact]
    public void FieldScopeIsBoundOnceAndUnknownScopeDoesNotReadRows()
    {
        var fields = new CountedFields(64); var rows = new CountedRows(100);
        Assert.Null(TableTextSearch.FindNext(fields, rows, new("absent"), onlyField: "F63"));
        Assert.InRange(fields.Reads, 64, 65); Assert.Equal(100, rows.Reads);
        rows.Reads = 0;
        Assert.Null(TableTextSearch.FindNext(fields, rows, new("absent"), onlyField: "unknown"));
        Assert.Equal(0, rows.Reads);
    }
    [Fact]
    public void AllFieldScanReusesTheCurrentDisplayRow()
    {
        var fields = new CountedFields(64); var rows = new CountedRows(100);
        Assert.Null(TableTextSearch.FindNext(fields, rows, new("absent")));
        Assert.Equal(100, rows.Reads);
    }
    [Theory]
    [InlineData(TextMatchMode.AnyPart)] [InlineData(TextMatchMode.WholeField)] [InlineData(TextMatchMode.StartOfField)]
    public void IdenticalMatchesDoNotConsumeEditOrCharacterBudgets(TextMatchMode mode)
    {
        var fields = new[] { new FieldDefinition { Name = "Text" } };
        var rows = new[] { new Record { Values = new() { ["Text"] = "A" } }, new Record { Values = new() { ["Text"] = "a" } } };
        var changes = TableTextSearch.Replacements(fields, rows, new("a", mode), "a", maximumEdits: 1, maximumCharacters: 1);
        Assert.Equal("a", Assert.Single(changes).Value);
        Assert.Equal(rows[0].Id, changes[0].RecordId);
    }
    [Fact]
    public void UnicodeDeletionAndExpansionAreLiteralAndBounded()
    {
        var fields = new[] { new FieldDefinition { Name = "Text" } };
        var rows = new[] { new Record { Values = new() { ["Text"] = "😀$😀" } } };
        Assert.Equal("$", Assert.Single(TableTextSearch.Replacements(fields, rows, new("😀"), "")).Value);
        Assert.Equal("$1$$1", Assert.Single(TableTextSearch.Replacements(fields, rows, new("😀"), "$1")).Value);
        Assert.Throws<DataSpaceException>(() => TableTextSearch.Replacements(fields, rows, new("😀"), "long", maximumCharacters: 8));
    }
    [Fact]
    public void CancellationAfterFetchingARecordCannotReturnAMatch()
    {
        using var cts = new CancellationTokenSource(); var rows = new CountedRows(1, () => cts.Cancel());
        Assert.ThrowsAny<OperationCanceledException>(() => TableTextSearch.FindNext(new CountedFields(1), rows, new("value"), onlyField: "F0", cancellationToken: cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => TableTextSearch.Replacements([], [], new("value"), "x", cancellationToken: cts.Token));
    }
    private sealed class CountedFields(int count) : IReadOnlyList<FieldDefinition>
    {
        public int Reads { get; private set; }
        public int Count => count;
        public FieldDefinition this[int index] { get { Reads++; return new() { Name = "F" + index }; } }
        public IEnumerator<FieldDefinition> GetEnumerator() { for (var i = 0; i < count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class CountedRows(int count, Action? onRead = null) : IReadOnlyList<Record>
    {
        public int Reads { get; set; }
        public int Count => count;
        public Record this[int index] { get { Reads++; onRead?.Invoke(); return new() { Values = new() { ["F0"] = "value" } }; } }
        public IEnumerator<Record> GetEnumerator() { for (var i = 0; i < count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
