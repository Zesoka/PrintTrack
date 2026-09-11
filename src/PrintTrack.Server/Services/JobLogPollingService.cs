using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintTrack.Server.Data;
using PrintTrack.Server.Options;

namespace PrintTrack.Server.Services;

/// <summary>Pulls the HP job-log CSV from every configured printer on a schedule and imports it.</summary>
public sealed class JobLogPollingService(
    IServiceProvider sp,
    JobLogFetcher fetcher,
    IOptions<PrintTrackOptions> options,
    ILogger<JobLogPollingService> logger) : BackgroundService
{
    private readonly JobLogOptions _opt = options.Value.JobLog;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled)
        {
            logger.LogInformation("Pull automático de Job Log deshabilitado (PrintTrack:JobLog:Enabled=false).");
            return;
        }

        try { await Task.Delay(TimeSpan.FromSeconds(40), ct); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(2, _opt.PollMinutes)));
        do
        {
            try { await PollAllAsync("scheduler", ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Ciclo de pull de Job Log falló."); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task<int> PollAllAsync(string triggeredBy, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.PrinterJobLogConfigs
            .Where(c => c.Enabled && c.BaseUrl != null && c.BaseUrl != "")
            .Select(c => c.PrinterId).ToListAsync(ct);

        var imported = 0;
        foreach (var id in ids)
        {
            var (n, _) = await PollOneAsync(id, triggeredBy, ct);
            imported += n;
        }
        if (ids.Count > 0)
            logger.LogInformation("Pull Job Log ({By}): {Imported} trabajos nuevos de {N} impresoras.", triggeredBy, imported, ids.Count);
        return imported;
    }

    /// <summary>Pull + import one printer now. Returns (importedCount, error-or-null).</summary>
    public async Task<(int Imported, string? Error)> PollOneAsync(int printerId, string triggeredBy, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var importer = scope.ServiceProvider.GetRequiredService<HpJobLogImporter>();

        var cfg = await db.PrinterJobLogConfigs.FirstOrDefaultAsync(c => c.PrinterId == printerId, ct);
        if (cfg is null) return (0, "La impresora no tiene configuración de Job Log.");

        cfg.LastPolledAt = DateTimeOffset.UtcNow;

        var (csv, fetchErr, snippet) = await fetcher.FetchCsvAsync(cfg, _opt.TimeoutSeconds, ct);
        cfg.LastResponseSnippet = snippet is { Length: > 0 } ? snippet[..Math.Min(snippet.Length, 70000)] : null;
        if (csv is null)
        {
            cfg.LastError = fetchErr;
            cfg.LastImported = 0;
            await db.SaveChangesAsync(ct);
            return (0, fetchErr);
        }

        var hadJobsBefore = await db.PrintJobs.AnyAsync(j => j.PrinterId == printerId && j.Source == JobSource.HpJobLog, ct);
        var result = await importer.ImportAsync(printerId, csv, onlyPrintJobs: true, triggeredBy, ct);

        if (!result.Ok)
        {
            cfg.LastError = result.FatalError;
            cfg.LastImported = 0;
        }
        else
        {
            cfg.LastImported = result.Imported;
            var printable = result.TotalRows - result.SkippedType;
            // If nothing was a duplicate and we already had history, the ring buffer probably
            // rolled past our previous pull → we may have missed jobs in between.
            cfg.LastError = hadJobsBefore && result.Imported > 0 && result.Duplicates == 0 && printable > 20
                ? "Posible hueco entre lecturas: bajá PrintTrack:JobLog:PollMinutes."
                : null;
        }
        await db.SaveChangesAsync(ct);
        return (result.Imported, cfg.LastError);
    }
}
