using DataSpace.Core;
using Microsoft.Data.Sqlite;

namespace DataSpace.DataSources.Relational;

public static class SqliteFiles
{
    public static Task<IDataSource> OpenAsync(string path, CancellationToken cancellationToken = default) => Task.Run<IDataSource>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path) || new FileInfo(path).Length > SourceLimits.MaxFileBytes) throw new DataSpaceException("Select an existing SQLite file no larger than 16 MiB.");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var connection = new SqliteConnection(connectionString); connection.Open();
        using (var options = connection.CreateCommand()) { options.CommandText = "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF;"; options.ExecuteNonQuery(); }
        using var list = connection.CreateCommand();
        list.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 129";
        var names = new List<string>(); using (var reader = list.ExecuteReader()) while (reader.Read()) names.Add(reader.GetString(0));
        if (names.Count is < 1 or > 128) throw new DataSpaceException("SQLite source requires 1–128 user tables.");
        var bindings = new List<TableBinding>();
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested(); using var columns = connection.CreateCommand();
            columns.CommandText = "SELECT name, pk FROM pragma_table_info(@name) ORDER BY pk"; columns.Parameters.AddWithValue("@name", name);
            var all = new List<string>(); var keys = new List<string>();
            using (var reader = columns.ExecuteReader()) while (reader.Read()) { all.Add(reader.GetString(0)); if (reader.GetInt32(1) > 0) keys.Add(reader.GetString(0)); }
            if (keys.Count == 0) { if (all.Any(c => c.Equals("_rowid_", StringComparison.OrdinalIgnoreCase))) throw new DataSpaceException("A table shadows _rowid_ and has no primary key. Add a key before browsing."); keys.Add("_rowid_"); }
            bindings.Add(new(name, name, "", keys.ToArray()));
        }
        return new RelationalDataSource(Path.GetFileName(path), SourceDialect.Sqlite, () => new SqliteConnection(connectionString), bindings);
    }, cancellationToken);

    /// <summary>Export a new database only. Never overwrites the selected source file. Decimal/date values remain lossless text.</summary>
    public static Task<byte[]> ExportAsync(string name, FieldDefinition[] fields, Record[] rows, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (fields.Length is < 1 or > SourceLimits.MaxColumns || rows.Length > SourceLimits.MaxImportRows) throw new DataSpaceException("SQLite export exceeds the table size limit.");
        var path = Path.Combine(Path.GetTempPath(), "dataspace-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open(); using var transaction = connection.BeginTransaction();
                using var create = connection.CreateCommand(); create.Transaction = transaction;
                create.CommandText = "CREATE TABLE " + RelationalDataSource.Quote(name, SourceDialect.Sqlite) + " (" + string.Join(",", fields.Select(f => RelationalDataSource.Quote(f.Name, SourceDialect.Sqlite) + " TEXT")) + ")"; create.ExecuteNonQuery();
                using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO " + RelationalDataSource.Quote(name, SourceDialect.Sqlite) + " VALUES (" + string.Join(",", fields.Select((_, i) => "@p" + i)) + ")";
                for (var i = 0; i < fields.Length; i++) insert.Parameters.Add(new SqliteParameter("@p" + i, SqliteType.Text));
                insert.Prepare();
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var i = 0; i < fields.Length; i++) insert.Parameters[i].Value = (object?)row[fields[i].Name] ?? DBNull.Value;
                    insert.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            if (new FileInfo(path).Length > SourceLimits.MaxFileBytes) throw new DataSpaceException("SQLite export exceeds 16 MiB.");
            return File.ReadAllBytes(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }, cancellationToken);
}
