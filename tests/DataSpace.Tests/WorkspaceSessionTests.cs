using DataSpace.Core;
using DataSpace.Storage;
using Xunit;

namespace DataSpace.Tests;

public sealed class WorkspaceSessionTests
{
    [Fact]
    public async Task SaveAndReloadPreserveTheWholeDocument()
    {
        var store = new MemoryWorkspaceStore();
        var workspace = new DatabaseWorkspace(SampleDatabase.Create());
        using var session = new WorkspaceSession(workspace, store);
        await session.InitializeAsync(); Assert.True(session.IsDirty);
        await session.SaveAsync(); Assert.False(session.IsDirty);
        var loaded = new DatabaseWorkspace(new DatabaseDocument());
        using var other = new WorkspaceSession(loaded, store);
        await other.InitializeAsync();
        Assert.Equal(DocumentCodec.Serialize(workspace.Document), DocumentCodec.Serialize(loaded.Document)); Assert.False(other.IsDirty);
    }
    [Fact]
    public async Task CompetingSaveDoesNotOverwriteOrMarkTheLoserClean()
    {
        var store = new MemoryWorkspaceStore();
        using var first = new WorkspaceSession(new(new()), store);
        using var second = new WorkspaceSession(new(new()), store);
        await first.InitializeAsync(); await second.InitializeAsync();
        first.Workspace.Edit("name", document => document.Name = "First");
        second.Workspace.Edit("name", document => document.Name = "Second");
        await first.SaveAsync();
        await Assert.ThrowsAsync<DataSpaceException>(() => second.SaveAsync());
        Assert.True(second.IsDirty); Assert.Null(second.StoredVersion);
        Assert.Equal("First", DocumentCodec.Deserialize((await store.LoadAsync())!.Json).Name);
    }
    [Fact]
    public async Task EditDuringSaveRemainsDirtyAndNextSaveUsesCommittedVersion()
    {
        var memory = new MemoryWorkspaceStore();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new DelegateStore
        {
            Load = () => memory.LoadAsync(),
            Save = async (json, version) => { started.TrySetResult(); await release.Task; return await memory.SaveAsync(json, version); }
        };
        var workspace = new DatabaseWorkspace(new DatabaseDocument { Name = "Before" });
        using var session = new WorkspaceSession(workspace, store); await session.InitializeAsync();
        var saving = session.SaveAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        workspace.Edit("name", document => document.Name = "After"); release.SetResult(); await saving;
        Assert.True(session.IsDirty); Assert.NotNull(session.StoredVersion);
        Assert.Equal("Before", DocumentCodec.Deserialize((await memory.LoadAsync())!.Json).Name);
        await session.SaveAsync(); Assert.False(session.IsDirty);
        Assert.Equal("After", DocumentCodec.Deserialize((await memory.LoadAsync())!.Json).Name);
    }
    [Fact]
    public async Task EditDuringInitializationIsNotSilentlyReplaced()
    {
        var read = new TaskCompletionSource<StoredWorkspace?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = new DatabaseWorkspace(new DatabaseDocument());
        using var session = new WorkspaceSession(workspace, new DelegateStore { Load = () => read.Task });
        var loading = session.InitializeAsync(); workspace.Edit("name", document => document.Name = "Unsaved");
        read.SetResult(new("version", DocumentCodec.Serialize(new DatabaseDocument { Name = "Stored" })));
        await Assert.ThrowsAsync<DataSpaceException>(() => loading);
        Assert.Equal("Unsaved", workspace.Document.Name); Assert.False(session.IsInitialized);
    }
    [Fact]
    public async Task CorruptStoredJsonCannotBeOverwrittenBySave()
    {
        var writes = 0;
        var store = new DelegateStore
        {
            Load = () => Task.FromResult<StoredWorkspace?>(new("version", "not-json")),
            Save = (_, _) => { writes++; return Task.FromResult("new-version"); }
        };
        var workspace = new DatabaseWorkspace(new DatabaseDocument()); var original = workspace.Document;
        using var session = new WorkspaceSession(workspace, store);
        await Assert.ThrowsAsync<DataSpaceException>(() => session.InitializeAsync());
        await Assert.ThrowsAsync<DataSpaceException>(() => session.SaveAsync());
        Assert.Same(original, workspace.Document); Assert.Equal(0, writes);
    }
    [Fact]
    public async Task FailedSaveRetainsExpectedVersionAndDirtyState()
    {
        var store = new DelegateStore { Save = (_, _) => throw new IOException("Quota") };
        using var session = new WorkspaceSession(new(new()), store); await session.InitializeAsync();
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync()); Assert.True(session.IsDirty); Assert.Null(session.StoredVersion);
    }
    private sealed class DelegateStore : IWorkspaceStore
    {
        public Func<Task<StoredWorkspace?>> Load { get; init; } = () => Task.FromResult<StoredWorkspace?>(null);
        public Func<string, string?, Task<string>> Save { get; init; } = (_, _) => Task.FromResult("version");
        public Task<StoredWorkspace?> LoadAsync(CancellationToken cancellationToken = default) => Load();
        public Task<string> SaveAsync(string json, string? expectedVersion, CancellationToken cancellationToken = default) => Save(json, expectedVersion);
    }
}
