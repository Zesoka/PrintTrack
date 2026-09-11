using System.Text.RegularExpressions;
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
            // Job Log HTML isn't reachable (e.g. this device demands an admin login we don't have).
            // Some printers still answer IPP without one — fall back to importing straight from
            // there so this printer isn't left completely unaudited.
            var (ippJobs, ippErr, _) = await ipp.GetCompletedJobsAsync(
                Uri.TryCreate(cfg.BaseUrl, UriKind.Absolute, out var bu) ? bu.Host : "", 631, limit: 50, _opt.TimeoutSeconds, ct);
            if (ippJobs is not null)
            {
                var ippResult = await importer.ImportFromIppAsync(printerId, ippJobs, triggeredBy, ct);
                cfg.LastImported = ippResult.Imported;
                cfg.LastError = $"Job Log no disponible ({fetchErr}) — importado por IPP: {ippResult.Imported} trabajo(s) nuevo(s).";
                await db.SaveChangesAsync(ct);
                return (ippResult.Imported, cfg.LastError);
            }

            cfg.LastError = ippErr is null ? fetchErr : $"{fetchErr} · IPP: {ippErr}";
            cfg.LastImported = 0;
            await db.SaveChangesAsync(ct);
            return (0, cfg.LastError);
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
        var (ippUpdated, ippNote) = await EnrichPagesFromIppAsync(db, importer, cfg, printerId, ct);
        if (ippUpdated > 0)
            logger.LogInformation("IPP: {Updated} trabajo(s) de la impresora {PrinterId} completados con páginas/hojas reales.", ippUpdated, printerId);
        if (ippNote is not null && cfg.LastError is null)
            cfg.LastError = ippNote;

        await db.SaveChangesAsync(ct);
        return (result.Imported, cfg.LastError);
    }

    // The device logs jobs submitted via IPP generically as "IPP-JOB-<id>-01" / "Invitado" in its
    // Job Log — the real user and document only ever show up over IPP itself. <id> lines up with
    // IPP's own job-id, so that's the reliable join key for those rows.
    private static readonly Regex IppPlaceholderName = new(@"^IPP-JOB-0*(\d+)-\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Ask the printer's IPP endpoint (port 631) for completed jobs and use them to fill in the
    /// real <c>Pages</c>/<c>Sheets</c> of already-imported jobs still at <c>Sheets == 0</c> (last
    /// week). Matches by IPP <c>job-id</c> against the device's "IPP-JOB-&lt;id&gt;-01" placeholder
    /// name when present — recovering the real user/document for those — else by printer + exact
    /// document name + normalized user, for jobs submitted the classic (non-IPP) way. HP's Job Log
    /// HTML never exposes a page count; IPP's <c>job-impressions-completed</c> / <c>job-media-sheets-completed</c> does.
    /// </summary>
    private async Task<(int Updated, string? Note)> EnrichPagesFromIppAsync(
        AppDbContext db, HpJobLogImporter importer, PrinterJobLogConfig cfg, int printerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || !Uri.TryCreate(cfg.BaseUrl, UriKind.Absolute, out var u))
            return (0, null);

        var (jobs, err, _) = await ipp.GetCompletedJobsAsync(u.Host, 631, limit: 50, timeoutSec: 15, ct);
        if (jobs is null) return (0, err is null ? null : $"IPP: {err}");

        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var pool = await db.PrintJobs
            .Where(j => j.PrinterId == printerId && j.Sheets == 0 && j.SubmittedAt >= cutoff)
            .ToListAsync(ct);

        var updated = 0;
        foreach (var job in jobs)
        {
            var jobId = job.GetInt("job-id");
            var userRaw = job.Get("job-originating-user-name");
            var doc = job.Get("job-name");
            var pages = job.GetInt("job-impressions-completed");
            var sheets = job.GetInt("job-media-sheets-completed") ?? pages;
            if (string.IsNullOrWhiteSpace(userRaw) || string.IsNullOrWhiteSpace(doc) || sheets is not > 0) continue;

            PrintJobRecord? match = jobId is > 0
                ? pool.FirstOrDefault(j => IppPlaceholderName.Match(j.DocumentName) is { Success: true } m
                                           && int.Parse(m.Groups[1].Value) == jobId)
                : null;

            if (match is null)
            {
                var norm = Naming.NormalizeUser(userRaw);
                var byDoc = pool.Where(j => j.DocumentName == doc).OrderByDescending(j => j.SubmittedAt).ToList();
                match = byDoc.FirstOrDefault(j => Naming.NormalizeUser(j.UserNameRaw) == norm) ?? byDoc.FirstOrDefault();
            }
            if (match is null) continue;

            if (IppPlaceholderName.IsMatch(match.DocumentName))
            {
                match.DocumentName = doc.Length > 512 ? doc[..512] : doc;
                match.UserNameRaw = userRaw;
                match.EndUserId = await importer.ResolveOrCreateUserAsync(userRaw, ct);
            }
            match.Pages = pages ?? sheets.Value;
            match.Sheets = sheets.Value;
            pool.Remove(match);   // one IPP job enriches at most one row per pull
            updated++;
        }
        return (updated, null);
    }
}
