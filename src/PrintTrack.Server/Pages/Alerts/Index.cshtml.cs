using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.Alerts;

/// <summary>One place with every printer currently in an error state (Job Log and/or SNMP), with a
/// plain-language explanation instead of having to open each Config page one by one.</summary>
public sealed class IndexModel(AppDbContext db, AdminScope scope) : PageModel
{
    public sealed record Row(
        Printer Printer, string Source, string RawError, string Label, string Explanation, DateTimeOffset? LastPolledAt);

    public List<Row> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await scope.LoadAsync();

        var jobLogErrors = await db.PrinterJobLogConfigs
            .Include(c => c.Printer).ThenInclude(p => p.Site)
            .Where(c => c.LastError != null && c.LastError != "")
            .Where(c => scope.IsSuperAdmin || (c.Printer.SiteId != null && scope.SiteIds.Contains(c.Printer.SiteId.Value)))
            .Select(c => new { c.Printer, Error = c.LastError!, c.LastPolledAt })
            .ToListAsync();

        var meterErrors = await db.PrinterMeterConfigs
            .Include(c => c.Printer).ThenInclude(p => p.Site)
            .Where(c => c.LastError != null && c.LastError != "")
            .Where(c => scope.IsSuperAdmin || (c.Printer.SiteId != null && scope.SiteIds.Contains(c.Printer.SiteId.Value)))
            .Select(c => new { c.Printer, Error = c.LastError!, c.LastPolledAt })
            .ToListAsync();

        Rows = jobLogErrors.Select(x =>
            {
                var (label, expl) = Ui.ExplainError(x.Error);
                return new Row(x.Printer, "Job Log", x.Error, label, expl, x.LastPolledAt);
            })
            .Concat(meterErrors.Select(x =>
            {
                var (label, expl) = Ui.ExplainError(x.Error);
                return new Row(x.Printer, "SNMP", x.Error, label, expl, x.LastPolledAt);
            }))
            // "Job Log no disponible ... importado por IPP: N" is informational, not a real failure —
            // keep it out of the main error count but still visible for reference lower down.
            .OrderBy(r => r.Label == "Job Log bloqueado, cubierto por IPP" ? 1 : 0)
            .ThenBy(r => r.Printer.Site?.Name)
            .ThenBy(r => r.Printer.Name)
            .ToList();
    }
}
