using DataSpace.Core;

namespace DataSpace.Storage;

public sealed record StoredWorkspace(string Version, string Json);

/// <summary>Optimistic concurrency contract. Save must atomically compare the expected version and replace the payload.</summary>
public interface IWorkspaceStore
{
    Task<StoredWorkspace?> LoadAsync(CancellationToken cancellationToken = default);
    Task<string> SaveAsync(string json, string? expectedVersion, CancellationToken cancellationToken = default);
}

public sealed class MemoryWorkspaceStore : IWorkspaceStore
{
    private readonly object _gate = new();
    private StoredWorkspace? _snapshot;
    public Task<StoredWorkspace?> LoadAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); lock (_gate) return Task.FromResult(_snapshot); }
    public Task<string> SaveAsync(string json, string? expectedVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_snapshot?.Version != expectedVersion) throw new DataSpaceException("The stored database changed in another window. Export your changes before reloading.");
            var version = Guid.NewGuid().ToString("N"); _snapshot = new(version, json); return Task.FromResult(version);
        }
    }
}

/// <summary>Local filesystem store using an inter-process lock, optimistic versions and same-directory atomic replacement.</summary>
public sealed class FileWorkspaceStore(string path) : IWorkspaceStore
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<StoredWorkspace?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadAsync(cancellationToken); }
        finally { _gate.Release(); }
    }
    private async Task<StoredWorkspace?> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(_path)) return null;
        var data = await File.ReadAllTextAsync(_path, token);
        var newline = data.IndexOf('\n');
        if (newline <= 0) throw new DataSpaceException("Invalid stored workspace envelope. The original file has not been overwritten.");
        return new(data[..newline], data[(newline + 1)..]);
    }
    public async Task<string> SaveAsync(string json, string? expectedVersion, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var current = await ReadAsync(cancellationToken);
            if (current?.Version != expectedVersion) throw new DataSpaceException("The database changed in another process. Export your changes before reloading.");
            var version = Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(temporary, version + "\n" + json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, _path, true); return version;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }
}
