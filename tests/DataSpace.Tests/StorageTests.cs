using DataSpace.Core;
using DataSpace.Storage;
using Xunit;

namespace DataSpace.Tests;

public sealed class StorageTests
{
    [Fact] public void CsvHandlesQuotesCommasUnicodeAndEmbeddedNewlines()
    {
        var table = CsvCodec.Import("Imported", "Name,Note,Number\r\nAnna,\"hello, world\",42\r\n\"O\"\"Brien\",\"line1\nline2\",7\r\nŁukasz,Polska,9\r\n");
        Assert.Equal(3, table.Records.Count); Assert.Equal("O\"Brien", table.Records[1]["Name"]);
        Assert.Equal("line1\nline2", table.Records[1]["Note"]); Assert.Equal(FieldType.Integer, table.Field("Number").Type);
        var roundtrip = CsvCodec.Import("Again", CsvCodec.Export(table.Fields, table.Records));
        Assert.Equal(table.Records.Select(r => r["Note"]), roundtrip.Records.Select(r => r["Note"]));
    }
    [Fact] public void CsvPreservesLeadingZeroIdentifiers()
    { var table = CsvCodec.Import("Codes", "Code\n00123\n00456"); Assert.Equal(FieldType.ShortText, table.Fields[0].Type); Assert.Equal("00123", table.Records[0]["Code"]); }
    [Theory][InlineData("A,B\n1")][InlineData("A,A\n1,2")][InlineData("A\n\"bad")][InlineData("A\n\"ok\"bad")]
    public void MalformedCsvIsRejected(string csv) => Assert.Throws<DataSpaceException>(() => CsvCodec.Import("Test", csv));
    [Fact] public void CsvFormulaProtectionIsDefaultAndOptOutIsExplicit()
    {
        var table = CsvCodec.Import("Safe", "Value\n=1+1");
        Assert.Contains("'=1+1", CsvCodec.Export(table.Fields, table.Records));
        Assert.DoesNotContain("'=1+1", CsvCodec.Export(table.Fields, table.Records, new() { ProtectSpreadsheetFormulas = false }));
    }
    [Fact] public void CsvSupportsTabSeparatedInputAndFinalEmptyFields()
    { var table = CsvCodec.Import("TSV", "A\tB\n1\t", new() { Delimiter = '\t' }); Assert.Single(table.Records); Assert.Null(table.Records[0]["B"]); }
    [Fact] public async Task MemoryStoreRejectsStaleWriters()
    {
        var store = new MemoryWorkspaceStore(); Assert.Null(await store.LoadAsync());
        var version = await store.SaveAsync("one", null); var next = await store.SaveAsync("two", version);
        await Assert.ThrowsAsync<DataSpaceException>(() => store.SaveAsync("lost", version));
        Assert.Equal("two", (await store.LoadAsync())!.Json); Assert.NotEqual(version, next);
    }
    [Fact] public async Task FileStoreUsesVersionedAtomicReplacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataspace-" + Guid.NewGuid());
        try
        {
            var store = new FileWorkspaceStore(Path.Combine(directory, "workspace"));
            var version = await store.SaveAsync("first", null);
            var other = new FileWorkspaceStore(Path.Combine(directory, "workspace"));
            Assert.Equal("first", (await other.LoadAsync())!.Json);
            await other.SaveAsync("second", version);
            await Assert.ThrowsAsync<DataSpaceException>(() => store.SaveAsync("lost", version));
            Assert.Equal("second", (await store.LoadAsync())!.Json); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
