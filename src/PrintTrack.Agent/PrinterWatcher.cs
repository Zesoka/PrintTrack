using System.Collections.Concurrent;
using PrintTrack.Agent.Native;

namespace PrintTrack.Agent;

public enum JobPhase { Held, Released, Denied, Completed }

public sealed class TrackedJob
{
    public required string JobRef { get; init; }
    public required uint JobId { get; init; }
    public required SpoolJob Snapshot { get; set; }
    public JobPhase Phase { get; set; } = JobPhase.Held;
    public DateTimeOffset FirstSeen { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    public int LastPagesPrinted { get; set; }
}

/// <summary>
/// Watches one print queue on a dedicated thread. New jobs are paused on sight and reported via
/// <see cref="NewJob"/>; the orchestrator answers by calling <see cref="Release"/> or <see cref="Deny"/>.
/// Completion of released jobs is detected and raised via <see cref="JobFinished"/>.
/// </summary>
public sealed class PrinterWatcher(string printerName, ILogger logger) : IDisposable
{
    private readonly ConcurrentDictionary<uint, TrackedJob> _tracked = new();
    private readonly ConcurrentQueue<(uint JobId, bool Release, string? Note)> _commands = new();
    private SpoolerPrinter? _printer;
    private Thread? _thread;
    private volatile bool _running;

    public string PrinterName => printerName;

    /// <summary>Raised (on the watcher thread) when a brand-new job has been paused and needs a decision.</summary>
    public event Func<PrinterWatcher, TrackedJob, Task>? NewJob;

    /// <summary>Raised when a previously released job leaves the queue (printed) or a held job is cancelled.</summary>
    public event Action<TrackedJob, bool /*printed*/, int /*actualPages*/>? JobFinished;

    public void Start()
    {
        _printer = SpoolerPrinter.Open(printerName);
        _printer.StartChangeNotifications();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = $"pt-watch:{printerName}" };
        _thread.Start();
        logger.LogInformation("Vigilando la cola '{Printer}'.", printerName);
    }

    public void Release(uint jobId, string? note = null) => _commands.Enqueue((jobId, true, note));
    public void Deny(uint jobId, string? note = null) => _commands.Enqueue((jobId, false, note));

    private void Loop()
    {
        while (_running)
        {
            try
            {
                _printer!.WaitForChange(750);
                DrainCommands();
                Reconcile();
                Cleanup();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en el bucle de la cola '{Printer}'.", printerName);
                try { Thread.Sleep(2000); } catch { /* ignore */ }
            }
        }
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out var cmd))
        {
            if (!_tracked.TryGetValue(cmd.JobId, out var tj)) continue;
            try
            {
                if (cmd.Release)
                {
                    _printer!.Resume(cmd.JobId);
                    tj.Phase = JobPhase.Released;
                    tj.ResolvedAt = DateTimeOffset.UtcNow;
                    logger.LogInformation("Trabajo {JobId} liberado en '{Printer}'.", cmd.JobId, printerName);
                }
                else
                {
                    _printer!.Delete(cmd.JobId);
                    tj.Phase = JobPhase.Denied;
                    tj.ResolvedAt = DateTimeOffset.UtcNow;
                    JobFinished?.Invoke(tj, false, tj.Snapshot.PagesPrinted);
                    logger.LogInformation("Trabajo {JobId} eliminado en '{Printer}' ({Note}).",
                        cmd.JobId, printerName, cmd.Note);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudo aplicar el comando al trabajo {JobId}.", cmd.JobId);
            }
        }
    }

    private void Reconcile()
    {
        List<SpoolJob> jobs;
        try { jobs = [.. _printer!.ListJobs()]; }
        catch (Exception ex) { logger.LogWarning(ex, "EnumJobs falló en '{Printer}'.", printerName); return; }

        var present = new HashSet<uint>();

        foreach (var job in jobs)
        {
            present.Add(job.JobId);

            if (_tracked.TryGetValue(job.JobId, out var existing))
            {
                existing.Snapshot = job;
                if (existing.Phase == JobPhase.Released)
                {
                    existing.LastPagesPrinted = Math.Max(existing.LastPagesPrinted, job.PagesPrinted);
                    if (job.IsPrinted)
                    {
                        existing.Phase = JobPhase.Completed;
                        JobFinished?.Invoke(existing, true, PickPageCount(existing));
                    }
                }
                continue;
            }

            // brand-new job
            if (string.IsNullOrWhiteSpace(job.UserName)) continue;      // system job
            if (job.IsPrinted || job.IsDeleted) continue;               // already done before we saw it

            bool paused;
            try { paused = _printer!.Pause(job.JobId); }
            catch (Exception ex) { logger.LogError(ex, "No se pudo pausar el trabajo {JobId}.", job.JobId); continue; }

            var refreshed = _printer!.FindJob(job.JobId) ?? job;
            var tj = new TrackedJob
            {
                JobRef = Guid.NewGuid().ToString("N"),
                JobId = job.JobId,
                Snapshot = refreshed
            };
            _tracked[job.JobId] = tj;

            if (!paused)
                logger.LogWarning("El trabajo {JobId} no pudo pausarse a tiempo; puede imprimirse parcialmente.", job.JobId);

            var handler = NewJob;
            if (handler is not null)
                _ = handler(this, tj);
        }

        // jobs that vanished from the queue
        foreach (var (id, tj) in _tracked)
        {
            if (present.Contains(id)) continue;
            switch (tj.Phase)
            {
                case JobPhase.Released:
                    tj.Phase = JobPhase.Completed;
                    JobFinished?.Invoke(tj, true, PickPageCount(tj));
                    break;
                case JobPhase.Held:
                    tj.Phase = JobPhase.Completed;
                    JobFinished?.Invoke(tj, false, 0); // disappeared while held == cancelled
                    break;
            }
        }
    }

    private void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        foreach (var (id, tj) in _tracked)
            if (tj.Phase is JobPhase.Completed or JobPhase.Denied
                && (tj.ResolvedAt ?? tj.FirstSeen) < cutoff)
                _tracked.TryRemove(id, out _);
    }

    private static int PickPageCount(TrackedJob tj)
    {
        var s = tj.Snapshot;
        if (s.PagesPrinted > 0) return s.PagesPrinted;
        if (tj.LastPagesPrinted > 0) return tj.LastPagesPrinted;
        return s.TotalPages;
    }

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(2000); } catch { /* ignore */ }
        _printer?.Dispose();
    }
}
