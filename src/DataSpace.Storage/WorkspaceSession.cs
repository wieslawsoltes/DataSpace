using DataSpace.Core;

namespace DataSpace.Storage;

/// <summary>Coordinates a document with optimistic persistent storage without losing edits made during I/O.</summary>
public sealed class WorkspaceSession(DatabaseWorkspace workspace, IWorkspaceStore store) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DatabaseDocument? _savedDocument;
    private string? _version;
    public DatabaseWorkspace Workspace { get; } = workspace;
    public bool IsInitialized { get; private set; }
    public bool IsDirty => !ReferenceEquals(_savedDocument, Workspace.Document);
    public string? StoredVersion => _version;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsInitialized) return;
            var before = Workspace.Document;
            var stored = await store.LoadAsync(cancellationToken);
            var loaded = stored is null ? null : DocumentCodec.Deserialize(stored.Json);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(before, Workspace.Document))
                throw new DataSpaceException("The document was edited while storage was loading. Export those edits before reloading.");
            if (loaded is not null)
            {
                Workspace.Replace(loaded);
                _savedDocument = Workspace.Document;
            }
            _version = stored?.Version;
            IsInitialized = true;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized) throw new DataSpaceException("Storage has not loaded successfully. Export a database copy to preserve your work.");
            if (!IsDirty) return;
            var snapshot = Workspace.Document;
            var json = DocumentCodec.Serialize(snapshot);
            var version = await store.SaveAsync(json, _version, cancellationToken);
            // Only this exact snapshot is saved. A later edit remains dirty.
            _version = version;
            _savedDocument = snapshot;
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}
