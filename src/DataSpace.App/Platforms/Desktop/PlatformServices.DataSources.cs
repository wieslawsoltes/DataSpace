using DataSpace.Core;
using DataSpace.DataSources;
using DataSpace.DataSources.Relational;
using Windows.Storage.Pickers;
namespace DataSpace.App;
internal static partial class PlatformServices
{
    public static async Task<IDataSource?> OpenSqliteAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".sqlite"); picker.FileTypeFilter.Add(".sqlite3"); picker.FileTypeFilter.Add(".db");
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        return await SqliteFiles.OpenAsync(file.Path);
    }
    public static Task<byte[]> ExportSqliteAsync(string name, FieldDefinition[] fields, Record[] rows, CancellationToken token) => SqliteFiles.ExportAsync(name, fields, rows, token);
}
