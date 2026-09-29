using DataSpace.Core;
using DataSpace.DataSources;
using DataSpace.DataSources.Relational;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DataSpace.DataSources.Tests;

public sealed class SqliteSourceTests
{
    [Fact]
    public async Task RealSqliteFileSupportsCatalogPagingLosslessValuesAndNoWriteBack()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, amount TEXT, bytes BLOB); INSERT INTO items VALUES (1,'Żółć','12345678901234567890.123456789',x'0001FF'),(9223372036854775807,'','0',null);"; command.ExecuteNonQuery();
            }
            var original = File.ReadAllBytes(path);
            await using var source = await SqliteFiles.OpenAsync(path); var tables = await source.GetTablesAsync(); Assert.Single(tables);
            var first = await source.ReadAsync(new("items", 0, 1)); var second = await source.ReadAsync(new("items", 1, 1));
            Assert.True(first.HasMore); Assert.False(second.HasMore); Assert.Equal("hex:0001FF", first.Rows[0][3]); Assert.Equal("9223372036854775807", second.Rows[0][0]);
            Assert.Equal("", second.Rows[0][1]); Assert.Null(second.Rows[0][3]);
            var copy = await SourceImport.ReadTableAsync(source, tables[0], "Imported"); Assert.Equal(2, copy.Records.Count);
            Assert.Equal(original, File.ReadAllBytes(path));
            await Assert.ThrowsAsync<DataSpaceException>(() => source.ReadAsync(new("items; DROP TABLE items")));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task ExportIsARealStandaloneSqliteDatabase()
    {
        var fields = new[] { new FieldDefinition { Name = "ID", Type = FieldType.Integer }, new FieldDefinition { Name = "Title" } };
        var rows = new[] { new Record { Values = new() { ["ID"] = "9223372036854775807", ["Title"] = "quoted ' value 😀" } } };
        var bytes = await SqliteFiles.ExportAsync("Exported", fields, rows); var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes); await using var source = await SqliteFiles.OpenAsync(path);
            var page = await source.ReadAsync(new("Exported")); Assert.Equal(rows[0]["ID"], page.Rows[0][0]); Assert.Equal(rows[0]["Title"], page.Rows[0][1]);
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(SourceDialect.Sqlite, "a\"b", "\"a\"\"b\"")]
    [InlineData(SourceDialect.PostgreSql, "a\"b", "\"a\"\"b\"")]
    [InlineData(SourceDialect.MySql, "a`b", "`a``b`")]
    [InlineData(SourceDialect.SqlServer, "a]b", "[a]]b]")]
    public void IdentifiersAreEscapedNotConcatenatedAsSql(SourceDialect dialect, string value, string expected) => Assert.Equal(expected, RelationalDataSource.Quote(value, dialect));
    [Fact]
    public void UnorderedRemoteTablesAreRefused() => Assert.Throws<DataSpaceException>(() => new RelationalDataSource("test", SourceDialect.PostgreSql, () => throw new Exception(), [new("t", "t", "public", [])]));
}
