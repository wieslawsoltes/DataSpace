namespace DataSpace.DataSources;

/// <summary>Small, explicit page cache. Refresh invalidates in-flight admission; callers receive detached row arrays.</summary>
public sealed class SourcePager(IDataSource source, int capacity = 4)
{
    private readonly int _capacity = capacity is >= 1 and <= 16 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<SourceRequest, SourcePage> _pages = [];
    private readonly Queue<SourceRequest> _order = [];
    private int _generation;
    public long Reads { get; private set; }
    public long CacheHits { get; private set; }
    public int CachedPages => _pages.Count;
    public void Invalidate() { Interlocked.Increment(ref _generation); }
    private int _cacheGeneration;
    public async Task<SourcePage> ReadAsync(SourceRequest request, CancellationToken cancellationToken = default)
    {
        request.Validate(); await _gate.WaitAsync(cancellationToken);
        try
        {
            var generation = Volatile.Read(ref _generation);
            if (_cacheGeneration != generation) { _pages.Clear(); _order.Clear(); _cacheGeneration = generation; }
            if (_pages.TryGetValue(request, out var cached)) { CacheHits++; return Copy(cached); }
            var page = await source.ReadAsync(request, cancellationToken); Reads++;
            SourceLimits.Validate(page, request.Limit); cancellationToken.ThrowIfCancellationRequested();
            if (generation == Volatile.Read(ref _generation))
            {
                while (_pages.Count >= _capacity) _pages.Remove(_order.Dequeue());
                _pages.Add(request, Copy(page)); _order.Enqueue(request);
            }
            return page;
        }
        finally { _gate.Release(); }
    }
    private static SourcePage Copy(SourcePage page) => new(page.Columns.ToArray(), page.Rows.Select(row => row.ToArray()).ToArray(), page.HasMore);
}
