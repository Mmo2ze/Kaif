using System.Collections.Concurrent;
using StoreShared.Print;

namespace StoreAPI.Services;

public sealed class LabelPrintQueueService
{
    private const int MaxJobs = 100;
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private readonly ConcurrentQueue<QueuedJob> _queue = new();
    private int _count;

    public int PendingCount => Volatile.Read(ref _count);

    public EnqueueLabelPrintResponse Enqueue(string barcode, int count, string requestedBy)
    {
        PurgeExpired();
        if (Volatile.Read(ref _count) >= MaxJobs)
            throw new InvalidOperationException("Print queue is full. Wait for the store computer to catch up.");

        var job = new QueuedJob(
            Guid.NewGuid(),
            barcode,
            Math.Clamp(count, 1, 500),
            DateTimeOffset.UtcNow,
            requestedBy);

        _queue.Enqueue(job);
        Interlocked.Increment(ref _count);

        return new EnqueueLabelPrintResponse(
            job.Id,
            "Label print queued. Store POS will send it to the barcode printer.");
    }

    /// <summary>Returns the next pending job without removing it (POS acks after a successful print).</summary>
    public LabelPrintJobDto? PeekNext()
    {
        PurgeExpired();

        if (_queue.TryPeek(out var job) && DateTimeOffset.UtcNow - job.CreatedAt <= MaxAge)
            return ToDto(job);

        return null;
    }

    /// <summary>Removes a job after Store POS printed it successfully.</summary>
    public bool TryAcknowledge(Guid jobId)
    {
        PurgeExpired();

        if (!_queue.TryPeek(out var head) || head.Id != jobId)
            return false;

        if (_queue.TryDequeue(out var job) && job.Id == jobId)
        {
            Interlocked.Decrement(ref _count);
            return true;
        }

        return false;
    }

    public bool IsQueued(Guid jobId)
    {
        PurgeExpired();
        foreach (var job in _queue)
        {
            if (job.Id == jobId)
                return true;
        }

        return false;
    }

    private static LabelPrintJobDto ToDto(QueuedJob job) =>
        new(job.Id, job.Barcode, job.Count, job.CreatedAt, job.RequestedBy);

    private void PurgeExpired()
    {
        if (_count == 0)
            return;

        var kept = new List<QueuedJob>();
        while (_queue.TryDequeue(out var job))
        {
            Interlocked.Decrement(ref _count);
            if (DateTimeOffset.UtcNow - job.CreatedAt <= MaxAge)
                kept.Add(job);
        }

        foreach (var job in kept)
        {
            _queue.Enqueue(job);
            Interlocked.Increment(ref _count);
        }
    }

    private sealed record QueuedJob(
        Guid Id,
        string Barcode,
        int Count,
        DateTimeOffset CreatedAt,
        string RequestedBy);
}
