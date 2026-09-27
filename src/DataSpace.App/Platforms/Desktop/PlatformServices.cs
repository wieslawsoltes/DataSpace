using DataSpace.Core;
using DataSpace.Storage;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DataSpace.App;

internal static class PlatformServices
{
    public static Task InitializeAsync() => Task.CompletedTask;
    public static IWorkspaceStore CreateStore() => new FileWorkspaceStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DataSpace", "workspace.dspace"));
    public static async Task<string?> ImportTextAsync(string kind)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        if (kind == "csv") picker.FileTypeFilter.Add(".csv");
        else { picker.FileTypeFilter.Add(".dspace"); picker.FileTypeFilter.Add(".json"); }
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > 64UL * 1024 * 1024) throw new DataSpaceException("Import is limited to 64 MiB.");
        return await FileIO.ReadTextAsync(file);
    }
    public static async Task ExportFileAsync(string name, string mimeType, byte[] bytes)
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(name), SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add("DataSpace export", new List<string> { Path.GetExtension(name) });
        var file = await picker.PickSaveFileAsync(); if (file is not null) await FileIO.WriteBytesAsync(file, bytes);
    }
    public static void SetDirty(bool dirty) { }
    public static void Ready(string name, bool storageLoaded) { }
    public static void ReportFailure(string message) => Console.Error.WriteLine(message);
}
