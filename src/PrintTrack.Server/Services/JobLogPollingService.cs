using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintTrack.Server.Data;
using PrintTrack.Server.Options;

namespace PrintTrack.Server.Services;

/// <summary>Pulls the HP job-log CSV from every configured printer on a schedule and imports it.</summary>
public sealed class JobLogPollingService(
    IServiceProvider sp,
    JobLogFetcher fetcher,
    IppClient ipp,
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

        var hadJobsBefore = await db.PrintJobs.AnyAsync(j => j.PrinterId == printerId, ct);
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

        // Best-effort: back-fill real Páginas/Hojas via IPP (Get-Jobs) on whatever this printer's
        // Job Log just imported (or imported earlier) with no page data yet. Never creates rows —
        // only updates existing ones — so it can't duplicate what the Job Log scrape already added.
        var (ippUpdated, ippNote) = await EnrichPagesFromIppAsync(db, cfg, printerId, ct);
        if (ippUpdated > 0)
            logger.LogInformation("IPP: {Updated} trabajo(s) de la impresora {PrinterId} completados con páginas/hojas reales.", ippUpdated, printerId);
        if (ippNote is not null && cfg.LastError is null)
            cfg.LastError = ippNote;

        await db.SaveChangesAsync(ct);
        return (result.Imported, cfg.LastError);
    }

    /// <summary>
    /// Ask the printer's IPP endpoint (port 631) for completed jobs and use them to fill in the
    /// real <c>Pages</c>/<c>Sheets</c> of already-imported jobs (matched by printer + normalized
    /// user + exact document name, among rows still at <c>Sheets == 0</c> from the last week).
    /// HP's Job Log HTML never exposes a page count; IPP's <c>job-impressions-completed</c> /
    /// <c>job-media-sheets-completed</c> does.
    /// </summary>
    private async Task<(int Updated, string? Note)> EnrichPagesFromIppAsync(
        AppDbContext db, PrinterJobLogConfig cfg, int printerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || !Uri.TryCreate(cfg.BaseUrl, UriKind.Absolute, out var u))
            return (0, null);

        var (jobs, err, _) = await ipp.GetCompletedJobsAsync(u.Host, 631, limit: 50, timeoutSec: 15, ct);
        if (jobs is null) return (0, err is null ? null : $"IPP: {err}");

        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var updated = 0;
        foreach (var job in jobs)
        {
            var userRaw = job.Get("job-originating-user-name");
            var doc = job.Get("job-name");
            var pages = job.GetInt("job-impressions-completed");
            var sheets = job.GetInt("job-media-sheets-completed") ?? pages;
            if (string.IsNullOrWhiteSpace(userRaw) || string.IsNullOrWhiteSpace(doc) || sheets is not > 0) continue;

            var norm = Naming.NormalizeUser(userRaw);
            var candidates = await db.PrintJobs
                .Where(j => j.PrinterId == printerId && j.Sheets == 0 && j.DocumentName == doc && j.SubmittedAt >= cutoff)
                .OrderByDescending(j => j.SubmittedAt)
                .ToListAsync(ct);
            var match = candidates.FirstOrDefault(j => Naming.NormalizeUser(j.UserNameRaw) == norm) ?? candidates.FirstOrDefault();
            if (match is null) continue;

            match.Pages = pages ?? sheets.Value;
            match.Sheets = sheets.Value;
            updated++;
        }
        return (updated, null);
    }
}
