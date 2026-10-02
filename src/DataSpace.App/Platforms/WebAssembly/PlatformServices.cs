using System.Runtime.InteropServices.JavaScript;
using DataSpace.Core;
using DataSpace.Storage;

namespace DataSpace.App;

internal static partial class PlatformServices
{
    private static bool _initialized;
    public static async Task InitializeAsync()
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document");
        var baseUri = document.GetPropertyAsString("baseURI") ?? throw new DataSpaceException("The browser document has no base URI.");
        await JSHost.ImportAsync("DataSpaceBrowser", new Uri(new Uri(baseUri), "browser-storage.js").AbsoluteUri);
        await JSHost.ImportAsync("DataSpaceInput", new Uri(new Uri(baseUri), "browser-input.js").AbsoluteUri);
        InstallInputAdapter();
        _initialized = true;
    }
    [JSImport("install", "DataSpaceInput")]
    private static partial void InstallInputAdapter();
    public static IWorkspaceStore CreateStore() => new BrowserWorkspaceStore();
    [JSImport("readWorkspace", "DataSpaceBrowser")]
    internal static partial Task<string> ReadWorkspaceAsync();
    [JSImport("writeWorkspace", "DataSpaceBrowser")]
    internal static partial Task<string> WriteWorkspaceAsync(string json, string expectedVersion);
    [JSImport("pickTextFile", "DataSpaceBrowser")]
    public static partial Task<string?> ImportTextAsync(string kind);
    [JSImport("downloadFile", "DataSpaceBrowser")]
    private static partial void DownloadFile(string name, string mimeType, string base64);
    [JSImport("setDirty", "DataSpaceBrowser")]
    public static partial void SetDirty(bool dirty);
    [JSImport("ready", "DataSpaceBrowser")]
    public static partial void Ready(string name, bool storageLoaded);
    [JSImport("reportFailure", "DataSpaceBrowser")]
    private static partial void Failure(string message);
    public static Task ExportFileAsync(string name, string mimeType, byte[] bytes)
    { DownloadFile(name, mimeType, Convert.ToBase64String(bytes)); return Task.CompletedTask; }
    public static void ReportFailure(string message) { if (_initialized) Failure(message); else Console.Error.WriteLine(message); }
}

internal sealed class BrowserWorkspaceStore : IWorkspaceStore
{
    public async Task<StoredWorkspace?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = await PlatformServices.ReadWorkspaceAsync();
        if (envelope.Length == 0) return null;
        var split = envelope.IndexOf('\n');
        if (split < 1) throw new DataSpaceException("Invalid browser storage envelope.");
        return new StoredWorkspace(envelope[..split], envelope[(split + 1)..]);
    }
    public async Task<string> SaveAsync(string json, string? expectedVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Completion means the IndexedDB read-write transaction committed. Never discard its returned version.
        return await PlatformServices.WriteWorkspaceAsync(json, expectedVersion ?? "");
    }
}
