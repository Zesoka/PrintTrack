using System.Collections.Concurrent;
using System.Drawing.Printing;
using Microsoft.Extensions.Options;
using PrintTrack.Shared;

namespace PrintTrack.Agent;

/// <summary>
/// Orchestrates the agent: enumerates local queues, starts a <see cref="PrinterWatcher"/> per queue,
/// and for every new job asks the server for a decision (allow / deny), then releases or deletes
/// the job and reports the outcome.
/// </summary>
public sealed class PrintMonitorService(
    IOptions<AgentOptions> options,
    ServerClient server,
    ILogger<PrintMonitorService> logger,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private readonly AgentOptions _opt = options.Value;
    private readonly ConcurrentDictionary<string, PrinterWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<CompleteJobRequest> _completionRetry = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Auditor de impresiones — agente iniciando. Servidor: {Url}", _opt.ServerUrl);

        SyncWatchers();
        await HeartbeatAsync(stoppingToken);

        using var hbTimer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _opt.HeartbeatMinutes)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainCompletionRetriesAsync(stoppingToken);
                if (!await hbTimer.WaitForNextTickAsync(stoppingToken)) break;
                SyncWatchers();
                await HeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Ciclo de mantenimiento del agente falló."); }
        }

        foreach (var w in _watchers.Values) w.Dispose();
    }

    // ---------- queue discovery ----------

    private IEnumerable<string> DiscoverPrinters()
    {
        IEnumerable<string> installed;
        try { installed = PrinterSettings.InstalledPrinters.Cast<string>(); }
        catch (Exception ex) { logger.LogError(ex, "No se pudieron enumerar las impresoras locales."); yield break; }

        foreach (var name in installed)
        {
            if (_opt.IgnorePrinters.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;
            if (_opt.Printers.Length > 0 &&
                !_opt.Printers.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase))) continue;
            yield return name;
        }
    }

    private void SyncWatchers()
    {
        var wanted = DiscoverPrinters().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in wanted.Where(n => !_watchers.ContainsKey(n)))
        {
            try
            {
                var watcher = new PrinterWatcher(name, loggerFactory.CreateLogger($"Watcher:{name}"));
                watcher.NewJob += OnNewJobAsync;
                watcher.JobFinished += OnJobFinished;
                watcher.Start();
                _watchers[name] = watcher;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudo iniciar la vigilancia de '{Printer}'.", name);
            }
        }

        foreach (var name in _watchers.Keys.Where(k => !wanted.Contains(k)).ToList())
            if (_watchers.TryRemove(name, out var w))
            {
                logger.LogInformation("Dejando de vigilar '{Printer}'.", name);
                w.Dispose();
            }
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        await server.HeartbeatAsync(new AgentHeartbeatRequest
        {
            WorkstationName = Environment.MachineName,
            AgentVersion = typeof(PrintMonitorService).Assembly.GetName().Version?.ToString() ?? "0.0",
            Printers = _watchers.Keys.ToArray(),
            OsVersion = Environment.OSVersion.VersionString
        }, ct);
    }

    // ---------- per-job decision flow ----------

    private async Task OnNewJobAsync(PrinterWatcher watcher, TrackedJob tj)
    {
        var s = tj.Snapshot;
        try
        {
            var request = new AuthorizeJobRequest
            {
                JobRef = tj.JobRef,
                UserName = s.UserName,
                WorkstationName = Environment.MachineName,
                PrinterName = watcher.PrinterName,
                DocumentName = s.Document,
                Pages = s.TotalPages,
                Copies = s.Copies,
                Color = s.Color,
                Duplex = s.Duplex,
                PaperSize = s.PaperSize,
                SizeBytes = s.SizeBytes,
                SubmittedAt = s.SubmittedUtc
            };

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_opt.ServerTimeoutSeconds));
            var decision = await server.AuthorizeAsync(request, timeout.Token);

            if (decision is null)
            {
                logger.LogWarning("Sin respuesta del servidor para {JobRef}; política: {Policy}.",
                    tj.JobRef, _opt.ServerUnreachable);
                if (_opt.ServerUnreachable == FailMode.Allow) watcher.Release(tj.JobId, "servidor no disponible");
                else watcher.Deny(tj.JobId, "servidor no disponible");
                return;
            }

            if (decision.Decision == JobDecisionType.Deny)
                watcher.Deny(tj.JobId, decision.Message);
            else
                watcher.Release(tj.JobId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo procesando el trabajo {JobRef}; se libera para no bloquear al usuario.", tj.JobRef);
            watcher.Release(tj.JobId, "error del agente");
        }
    }

    private void OnJobFinished(TrackedJob tj, bool printed, int actualPages)
    {
        var status = printed ? PrintJobStatus.Printed : PrintJobStatus.Cancelled;
        _ = ReportCompletionAsync(new CompleteJobRequest
        {
            JobRef = tj.JobRef,
            Status = status,
            ActualPages = actualPages > 0 ? actualPages : null
        });
    }

    private async Task ReportCompletionAsync(CompleteJobRequest req)
    {
        if (!await server.CompleteAsync(req, CancellationToken.None))
            _completionRetry.Enqueue(req);
    }

    private async Task DrainCompletionRetriesAsync(CancellationToken ct)
    {
        var carry = new List<CompleteJobRequest>();
        while (_completionRetry.TryDequeue(out var req))
            if (!await server.CompleteAsync(req, ct)) carry.Add(req);
        foreach (var r in carry) _completionRetry.Enqueue(r);
    }
}
