using DataSpace.Core;
using Xunit;

namespace DataSpace.DataSources.Tests;

public sealed class ImportPlanTests
{
    private static readonly SourceTable Table = new("data", "Data", [new("Code", "text"), new("Count", "integer"), new("Unused", "integer")], []);
    private static SourceImportPlan Plan() => new() { Fields = [new("Code", "ExternalCode", FieldType.ShortText), new("Count", "Quantity", FieldType.Integer)] };
    private static FakeSource Source() => new(_ => Task.FromResult(new SourcePage(Table.Columns, [["0001", "2", "invalid-unused-number"], ["0002", "3", null]], false)));

    [Fact]
    public async Task SelectedFieldsRenameConvertAndRetainSourceCaptions()
    {
        await using var source = Source(); var plan = Plan();
        plan.Fields.Reverse();
        var table = await SourceImport.ReadTableAsync(source, Table, "Copy", plan);
        Assert.Equal(new[] { "Quantity", "ExternalCode" }, table.Fields.Select(f => f.Name));
        Assert.Equal(new[] { "Count", "Code" }, table.Fields.Select(f => f.Caption));
        Assert.Equal("0001", table.Records[0]["ExternalCode"]); Assert.Equal("2", table.Records[0]["Quantity"]);
        Assert.All(table.Records, row => Assert.Equal(2, row.Values.Count));
        Assert.Equal(2, table.Records.Select(row => row.Id).Distinct().Count());
    }

    [Fact]
    public async Task SkippedMalformedTypedValuesAreNotConverted()
    {
        await using var source = Source(); var plan = SourceImportPlan.CreateDefault(Table);
        plan.Fields[2] = plan.Fields[2] with { Include = false };
        Assert.Equal(2, (await SourceImport.ReadTableAsync(source, Table, "Copy", plan)).Fields.Count);
        await Assert.ThrowsAsync<DataSpaceException>(() => SourceImport.ReadTableAsync(source, Table, "Copy"));
    }

    [Fact]
    public async Task AutoNumberIsFirstContinuousAcrossPagesAndReadyForNextInsert()
    {
        var calls = 0;
        await using var source = new FakeSource(request =>
        {
            calls++;
            var rows = Enumerable.Range(request.Offset, Math.Min(request.Limit, 503 - request.Offset))
                .Select(i => new string?[] { i.ToString(), "1", null }).ToArray();
            return Task.FromResult(new SourcePage(Table.Columns, rows, request.Offset + rows.Length < 503));
        });
        var plan = Plan(); plan.GeneratedKeyName = "ID";
        var result = await SourceImport.ReadTableAsync(source, Table, "Copy", plan);
        Assert.Equal(2, calls); Assert.Equal(FieldType.AutoNumber, result.Fields[0].Type);
        Assert.True(result.Fields[0].PrimaryKey); Assert.Equal(504, result.NextAutoNumber);
        Assert.Equal("1", result.Records[0]["ID"]); Assert.Equal("503", result.Records[502]["ID"]);
        Assert.Equal("504", RecordOperations.Insert(result, new Dictionary<string,string?> { ["ExternalCode"] = "new", ["Quantity"] = "1" })["ID"]);
    }

    [Theory]
    [InlineData("7", "7")]
    [InlineData("7", "007")]
    [InlineData(null, "8")]
    public async Task ExistingNumericKeyRejectsDuplicateOrMissingValues(string? first, string? second)
    {
        await using var source = new FakeSource(_ => Task.FromResult(new SourcePage(Table.Columns, [["a", first, null], ["b", second, null]], false)));
        var plan = Plan(); plan.Fields[1] = plan.Fields[1] with { PrimaryKey = true };
        await Assert.ThrowsAsync<DataSpaceException>(() => SourceImport.ReadTableAsync(source, Table, "Copy", plan));
    }

    [Fact]
    public async Task ExistingKeyHasRequiredUniquePropertiesAndOneUndoStep()
    {
        await using var source = Source(); var plan = Plan(); plan.Fields[0] = plan.Fields[0] with { PrimaryKey = true };
        var result = await SourceImport.ReadTableAsync(source, Table, "Copy", plan);
        Assert.True(result.Fields[0].Required); Assert.True(result.Fields[0].Unique);
        var workspace = new DatabaseWorkspace(new()); workspace.Edit("Import", document => document.Tables.Add(result));
        Assert.Single(workspace.Document.Tables); workspace.Undo(); Assert.Empty(workspace.Document.Tables);
        workspace.Redo(); Assert.Equal("0002", workspace.Document.Tables[0].Records[1]["ExternalCode"]);
    }

    [Theory]
    [InlineData("empty")][InlineData("skip-all")][InlineData("unknown")][InlineData("duplicate-source")]
    [InlineData("duplicate-name")][InlineData("invalid-name")][InlineData("auto-source")][InlineData("bad-type")]
    [InlineData("two-keys")][InlineData("key-conflict")][InlineData("key-name-conflict")][InlineData("length")]
    public async Task InvalidPlansFailBeforeSourceReads(string scenario)
    {
        await using var source = Source(); var plan = Plan();
        switch (scenario)
        {
            case "empty": plan.Fields.Clear(); break;
            case "skip-all": plan.Fields = plan.Fields.Select(f => f with { Include = false }).ToList(); break;
            case "unknown": plan.Fields[0] = plan.Fields[0] with { SourceName = "missing" }; break;
            case "duplicate-source": plan.Fields.Add(plan.Fields[0] with { Name = "Other" }); break;
            case "duplicate-name": plan.Fields[1] = plan.Fields[1] with { Name = "externalcode" }; break;
            case "invalid-name": plan.Fields[0] = plan.Fields[0] with { Name = "a.b" }; break;
            case "auto-source": plan.Fields[0] = plan.Fields[0] with { Type = FieldType.AutoNumber }; break;
            case "bad-type": plan.Fields[0] = plan.Fields[0] with { Type = (FieldType)999 }; break;
            case "two-keys": plan.Fields = plan.Fields.Select(f => f with { PrimaryKey = true }).ToList(); break;
            case "key-conflict": plan.GeneratedKeyName = "ID"; plan.Fields[0] = plan.Fields[0] with { PrimaryKey = true }; break;
            case "key-name-conflict": plan.GeneratedKeyName = "ExternalCode"; break;
            case "length": plan.Fields[0] = plan.Fields[0] with { MaxLength = 0 }; break;
        }
        await Assert.ThrowsAsync<DataSpaceException>(() => SourceImport.ReadTableAsync(source, Table, "Copy", plan));
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task BadConversionReportsRowAndFieldAndDoesNotMutatePlan()
    {
        await using var source = Source(); var plan = Plan(); plan.Fields[0] = plan.Fields[0] with { MaxLength = 2 };
        var error = await Assert.ThrowsAsync<DataSpaceException>(() => SourceImport.ReadTableAsync(source, Table, "Copy", plan));
        Assert.Contains("row 1", error.Message); Assert.Contains("ExternalCode", error.Message);
        Assert.Equal(2, plan.Fields[0].MaxLength);
    }

    [Fact]
    public async Task DraftAndCatalogMutationsDuringReadDoNotChangeInFlightImport()
    {
        var completion = new TaskCompletionSource<SourcePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var source = new FakeSource(_ => completion.Task);
        var table = Table with { Columns = Table.Columns.ToArray() }; var plan = Plan();
        var pending = SourceImport.ReadTableAsync(source, table, "Copy", plan);
        plan.Fields.Clear(); plan.GeneratedKeyName = "Surprise"; table.Columns[0] = new("Changed");
        completion.SetResult(new(Table.Columns, [["001", "2", null]], false));
        var result = await pending;
        Assert.Equal(new[] { "ExternalCode", "Quantity" }, result.Fields.Select(f => f.Name));
        Assert.Equal("001", result.Records[0]["ExternalCode"]);
    }

    [Fact]
    public async Task CancellationAfterLastAdapterReadStillFails()
    {
        using var cancel = new CancellationTokenSource();
        await using var source = new FakeSource(_ => { cancel.Cancel(); return Task.FromResult(new SourcePage(Table.Columns, [["a", "1", null]], false)); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SourceImport.ReadTableAsync(source, Table, "Copy", Plan(), cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task FinalProgressCallbackCanCancelBeforeSuccess()
    {
        using var cancel = new CancellationTokenSource(); await using var source = Source();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SourceImport.ReadTableAsync(source, Table, "Copy", Plan(), progress: new CallbackProgress(_ => cancel.Cancel()), cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task RetainedValueBudgetAppliesAcrossIndividuallyValidPages()
    {
        var large = new string('x', 1_000_000);
        var metadata = new SourceTable("data", "data", [new("Value")], []);
        await using var source = new FakeSource(_ => Task.FromResult(new SourcePage(metadata.Columns, [[large]], true)));
        await Assert.ThrowsAsync<DataSpaceException>(() => SourceImport.ReadTableAsync(source, metadata, "Copy"));
        Assert.Equal(17, source.Reads);
    }

    [Fact]
    public async Task SchemaDriftIncludesSkippedFields()
    {
        await using var source = new FakeSource(_ => Task.FromResult(new SourcePage([new("Code"), new("Count", "integer"), new("Different")], [], false)));
        await Assert.ThrowsAsync<DataSpaceException>(() => SourceImport.ReadTableAsync(source, Table, "Copy", Plan()));
    }

    private sealed class CallbackProgress(Action<int> action) : IProgress<int> { public void Report(int value) => action(value); }
    private sealed class FakeSource(Func<SourceRequest, Task<SourcePage>> read) : IDataSource
    {
        public int Reads { get; private set; }
        public string DisplayName => "Test";
        public Task<SourceTable[]> GetTablesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new[] { Table });
        public Task<SourcePage> ReadAsync(SourceRequest request, CancellationToken cancellationToken = default) { Reads++; return read(request); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
