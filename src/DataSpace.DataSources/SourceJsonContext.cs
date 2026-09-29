using System.Text.Json.Serialization;

namespace DataSpace.DataSources;

/// <summary>Portable SQLite export envelope. Text/null storage preserves source values without numeric rounding.</summary>
public sealed record SqliteExport(string Name, string[] Columns, string?[][] Rows);

// Every external-source wire contract is rooted explicitly for trimmed WebAssembly.
[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SourceTable[]))]
[JsonSerializable(typeof(SourcePage))]
[JsonSerializable(typeof(SourceRequest))]
[JsonSerializable(typeof(GatewaySource[]))]
[JsonSerializable(typeof(SqliteExport))]
internal partial class SourceJsonContext : JsonSerializerContext { }
