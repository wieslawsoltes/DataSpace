using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataSpace.Core;

public static class DocumentCodec
{
    public const int MaximumFileCharacters = 64 * 1024 * 1024;
    public static string Serialize(DatabaseDocument document) => JsonSerializer.Serialize(document, DocumentJsonContext.Default.DatabaseDocument);
    public static DatabaseDocument Deserialize(string json)
    {
        if (json.Length > MaximumFileCharacters) throw new DataSpaceException("The database exceeds the 64 MiB import limit.");
        try
        {
            var document = JsonSerializer.Deserialize(json, DocumentJsonContext.Default.DatabaseDocument)
                ?? throw new DataSpaceException("The database is empty.");
            SchemaValidator.Validate(document);
            return document;
        }
        catch (JsonException error) { throw new DataSpaceException("Invalid DataSpace database: " + error.Message); }
    }
    public static DatabaseDocument Clone(DatabaseDocument document) => Deserialize(Serialize(document));
}

[JsonSourceGenerationOptions(WriteIndented = false, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DatabaseDocument))]
internal partial class DocumentJsonContext : JsonSerializerContext;
