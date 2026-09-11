using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintTrack.Server.Data;
using PrintTrack.Server.Options;
using PrintTrack.Shared;

namespace PrintTrack.Server.Services;

/// <summary>
/// Core audit engine: for every job the agent reports, resolve the user and printer, apply the
/// only two blocking rules (user blocked / printer disabled), and log the job. No pricing, no
/// quota — this build only counts impressions, sheets, who and where.
/// </summary>
public sealed class AuditService(
    AppDbContext db,
    IOptions<PrintTrackOptions> options,
    ILogger<AuditService> logger)
{
    private readonly PrintTrackOptions _opt = options.Value;

    public async Task<AuthorizeJobResponse> AuthorizeJobAsync(
        AuthorizeJobRequest req, int? agentSiteId, CancellationToken ct)
    {
        var norm = Naming.NormalizeUser(req.UserName);
        var user = await db.EndUsers.FirstOrDefaultAsync(u => u.NormalizedUserName == norm, ct);

        if (user is null)
        {
            if (!_opt.AutoCreateUsers)
                return new AuthorizeJobResponse { Decision = JobDecisionType.Deny, Message = $"Usuario '{req.UserName}' no registrado." };
            user = new EndUser
            {
                UserName = req.UserName,
                NormalizedUserName = norm,
                AutoCreated = true,
                LastSeenAt = DateTimeOffset.UtcNow
            };
            db.EndUsers.Add(user);
            await db.SaveChangesAsync(ct);
        }

        user.LastSeenAt = DateTimeOffset.UtcNow;

        var printer = await ResolvePrinterAsync(req.PrinterName, req.WorkstationName, req.PrinterShareName, ct);
        var sheets = Math.Max(1, Math.Max(1, req.Pages) * Math.Max(1, req.Copies));

        JobDecisionType decision;
        PrintJobStatus status;
        string? message = null;

        if (user.IsBlocked)
        {
            decision = JobDecisionType.Deny;
            status = PrintJobStatus.Denied;
            message = "Cuenta bloqueada por el administrador.";
        }
        else if (printer.IsDisabled)
        {
            decision = JobDecisionType.Deny;
            status = PrintJobStatus.Denied;
            message = "Impresora deshabilitada.";
        }
        else
        {
            decision = JobDecisionType.Allow;
            status = PrintJobStatus.Pending;
        }

        if (printer.IsTracked || decision == JobDecisionType.Deny)
        {
            db.PrintJobs.Add(new PrintJobRecord
            {
                JobRef = req.JobRef,
                EndUserId = user.Id,
                UserNameRaw = req.UserName,
                PrinterId = printer.Id,
                PrinterNameRaw = req.PrinterName,
                WorkstationName = req.WorkstationName,
                SiteId = agentSiteId,
                DocumentName = Truncate(req.DocumentName, 512),
                Pages = req.Pages,
                Copies = Math.Max(1, req.Copies),
                Sheets = sheets,
                Color = req.Color,
                Duplex = req.Duplex,
                PaperSize = req.PaperSize,
                SizeBytes = req.SizeBytes,
                Status = status,
                DecisionMessage = message,
                SubmittedAt = req.SubmittedAt,
                DecidedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Authorize {JobRef} user={User} printer={Printer} sheets={Sheets} -> {Decision}",
            req.JobRef, norm, printer.Name, sheets, decision);

        return new AuthorizeJobResponse { Decision = decision, Sheets = sheets, Message = message };
    }

    public async Task CompleteJobAsync(CompleteJobRequest req, CancellationToken ct)
    {
        var job = await db.PrintJobs.FirstOrDefaultAsync(j => j.JobRef == req.JobRef, ct);
        if (job is null) return; // untracked printer or unknown job — nothing to finalize

        if (job.Status is PrintJobStatus.Printed or PrintJobStatus.Cancelled or PrintJobStatus.Denied)
            return; // idempotent

        job.Status = req.Status;
        job.CompletedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(req.Note)) job.DecisionMessage = req.Note;

        if (req.ActualPages is int actual && actual > 0)
        {
            job.Pages = actual;
            job.Sheets = Math.Max(1, actual * Math.Max(1, job.Copies));
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Printer> ResolvePrinterAsync(string name, string workstation, string? share, CancellationToken ct)
    {
        var isServerQueue = !string.IsNullOrWhiteSpace(share);
        var key = isServerQueue ? null : workstation;

        var printer = await db.Printers
            .FirstOrDefaultAsync(p => p.Name == name && p.WorkstationName == key, ct);

        if (printer is null)
        {
            printer = new Printer
            {
                Name = name,
                WorkstationName = key,
                ShareName = share,
                FirstSeenAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow
            };
            db.Printers.Add(printer);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            printer.LastSeenAt = DateTimeOffset.UtcNow;
            if (share is not null && printer.ShareName != share) printer.ShareName = share;
        }

        return printer;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
