using System.Data.Common;
using DataSpace.DataSources.Relational;
using DataSpace.Gateway;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace DataSpace.DataSources.Tests;

public sealed class OnlineSourceTests
{
    [Theory, Trait("Category", "Online")]
    [InlineData("postgresql", "DS_TEST_POSTGRES", SourceDialect.PostgreSql, "public")]
    [InlineData("mysql", "DS_TEST_MYSQL", SourceDialect.MySql, "")]
    [InlineData("sqlserver", "DS_TEST_SQLSERVER", SourceDialect.SqlServer, "dbo")]
    public async Task RealServerCatalogAndPagedReads(string provider, string environment, SourceDialect dialect, string schema)
    {
        var connectionString = Environment.GetEnvironmentVariable(environment) ?? throw new InvalidOperationException("Required integration database is not configured: " + environment);
        await using DbConnection connection = provider switch { "postgresql" => new NpgsqlConnection(connectionString), "mysql" => new MySqlConnection(connectionString), _ => new SqlConnection(connectionString) };
        // SQL Server's container can take longer than its TCP port to become ready.
        for (var retry = 0; ; retry++)
        {
            try { await connection.OpenAsync(); break; }
            catch when (retry < 30) { await Task.Delay(2000); }
        }
        var name = "DataSpace_Source_Test"; var quoted = (schema == "" ? "" : RelationalDataSource.Quote(schema, dialect) + ".") + RelationalDataSource.Quote(name, dialect);
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE " + quoted + " (id BIGINT PRIMARY KEY, title VARCHAR(100), amount DECIMAL(28,9))";
            await setup.ExecuteNonQueryAsync();
        }
        try
        {
            for (var i = 1; i <= 11; i++)
            {
                await using var insert = connection.CreateCommand(); insert.CommandText = "INSERT INTO " + quoted + " VALUES (@id,@title,@amount)";
                foreach (var (key, value) in new (string, object)[] { ("@id", (long)i), ("@title", i == 2 ? DBNull.Value : "item " + i), ("@amount", 1234567890.123456789m) })
                { var p = insert.CreateParameter(); p.ParameterName = key; p.Value = value; insert.Parameters.Add(p); }
                await insert.ExecuteNonQueryAsync();
            }
            await using var source = GatewayHost.CreateSource(new() { Id = "test", Name = "Test", Provider = provider, ConnectionString = connectionString, Tables = [new("items", name, schema, ["id"])] });
            var tables = await source.GetTablesAsync(); Assert.Single(tables); Assert.Equal(3, tables[0].Columns.Length);
            var first = await source.ReadAsync(new("items", 0, 5)); var second = await source.ReadAsync(new("items", 5, 5)); var last = await source.ReadAsync(new("items", 10, 5));
            Assert.Equal(5, first.Rows.Length); Assert.True(first.HasMore); Assert.Null(first.Rows[1][1]); Assert.Equal("6", second.Rows[0][0]); Assert.Single(last.Rows); Assert.False(last.HasMore);
            Assert.Equal("1234567890.123456789", first.Rows[0][2]);
            await Assert.ThrowsAsync<DataSpace.Core.DataSpaceException>(() => source.ReadAsync(new("items; DROP TABLE " + name)));
        }
        finally { await using var drop = connection.CreateCommand(); drop.CommandText = "DROP TABLE " + quoted; await drop.ExecuteNonQueryAsync(); }
    }
}
