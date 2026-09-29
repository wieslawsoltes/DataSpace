namespace DataSpace.DataSources;

/// <summary>Deterministic names for standalone SQLite exports.</summary>
public static class SqliteExportNames
{
    /// <summary>Prefix SQLite's reserved sqlite_ names; otherwise preserve the requested name.</summary>
    public static string TableName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase) ? "DataSpace_" + name : name;
    }
}
