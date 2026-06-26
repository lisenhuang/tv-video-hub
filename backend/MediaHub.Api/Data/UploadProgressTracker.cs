using System.Collections.Concurrent;

namespace MediaHub.Api.Data;

/// <summary>
/// Tracks in-flight <b>server→object-storage</b> upload progress, keyed by a
/// client-supplied upload id, so the admin dashboard can poll
/// <c>GET /api/admin/uploads/{id}/progress</c> and show a real percentage while the
/// backend streams a just-received file up to R2/local storage (the "Processing…"
/// phase the browser otherwise can't see).
///
/// Purely in-memory and best-effort: a missing/unknown id reports zero/not-found, and
/// entries self-expire so the map can never grow unbounded. Singleton.
/// </summary>
public sealed class UploadProgressTracker
{
    public readonly record struct Snapshot(long Transferred, long Total, bool Done, bool Failed, bool Found);

    private sealed class Entry
    {
        public long Transferred;
        public long Total;
        public bool Done;
        public bool Failed;
        public DateTimeOffset UpdatedAt;
    }

    // Keep finished/abandoned entries around briefly so a late poll still reads the
    // final state, then evict them lazily on the next write.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Entry> _map = new(StringComparer.Ordinal);

    /// <summary>Report bytes streamed to storage so far. No-op for an empty id.</summary>
    public void Report(string? id, long transferred, long total)
    {
        if (string.IsNullOrEmpty(id)) return;
        var e = _map.GetOrAdd(id, static _ => new Entry());
        e.Transferred = transferred;
        e.Total = total;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        Sweep();
    }

    /// <summary>Mark the storage upload finished (pins it at 100%). No-op for an empty id.</summary>
    public void Complete(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        var e = _map.GetOrAdd(id, static _ => new Entry());
        if (e.Total > 0) e.Transferred = e.Total;
        e.Done = true;
        e.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Mark the storage upload failed. No-op for an empty id.</summary>
    public void Fail(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        var e = _map.GetOrAdd(id, static _ => new Entry());
        e.Failed = true;
        e.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Snapshot Get(string? id)
    {
        if (!string.IsNullOrEmpty(id) && _map.TryGetValue(id, out var e))
            return new Snapshot(e.Transferred, e.Total, e.Done, e.Failed, Found: true);
        return new Snapshot(0, 0, false, false, Found: false);
    }

    private void Sweep()
    {
        var cutoff = DateTimeOffset.UtcNow - Ttl;
        foreach (var kv in _map)
            if (kv.Value.UpdatedAt < cutoff)
                _map.TryRemove(kv.Key, out _);
    }
}

/// <summary>
/// Read-only pass-through <see cref="Stream"/> that reports how far a consumer has read
/// it, so the backend can surface server→storage upload progress: the object-storage
/// client reads this stream as it PUTs the bytes to R2/local, so "bytes read" tracks
/// "bytes uploaded". Reports the inner position (falling back to a running count for
/// non-seekable streams) against a known total.
///
/// Deliberately does NOT dispose the inner stream — the caller's <c>await using</c> owns
/// its lifetime; this wrapper only observes reads.
/// </summary>
internal sealed class ProgressStream(Stream inner, long total, Action<long, long> report) : Stream
{
    private long _read;            // cumulative bytes read (non-seekable fallback)
    private long _lastReported = -1;

    private void Advance(int n)
    {
        if (n > 0) _read += n;
        var pos = inner.CanSeek ? inner.Position : _read;
        if (pos != _lastReported)
        {
            _lastReported = pos;
            report(pos, total);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, count);
        Advance(n);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var n = await inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        Advance(n);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var n = await inner.ReadAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
        Advance(n);
        return n;
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
