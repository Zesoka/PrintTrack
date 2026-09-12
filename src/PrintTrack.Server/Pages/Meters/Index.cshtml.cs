using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.Meters;

public sealed class IndexModel(AppDbContext db, MeterPollingService poller, AdminScope scope) : PageModel
{
    public sealed record Row(Printer Printer, PrinterMeterConfig? Cfg, MeterReading? Last);
    public List<Row> Rows { get; private set; } = [];
    public bool CanWrite { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostPollAllAsync(CancellationToken ct)
    {
        await scope.LoadAsync(ct);
        if (!scope.CanWrite) return Forbid();
        // A Sede-scoped admin only re-polls their own printers, not the whole fleet.
        var n = scope.IsSuperAdmin
            ? await poller.PollAllAsync(User.Identity?.Name ?? "admin", ct)
            : await PollScopedAsync(ct);
        TempData["Msg"] = $"Sondeo completado: {n} lectura(s) nueva(s).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPollOneAsync(int id, CancellationToken ct)
    {
        await scope.LoadAsync(ct);
        if (!scope.CanWrite) return Forbid();
        var printer = await db.Printers.FindAsync([id], ct);
        if (printer is null) return NotFound();
        if (!scope.CanSeeSite(printer.SiteId)) return Forbid();

        var err = await poller.PollOneAsync(id, User.Identity?.Name ?? "admin", ct);
        if (err is null) TempData["Msg"] = "Lectura SNMP guardada.";
        else TempData["Err"] = $"SNMP: {err}";
        return RedirectToPage();
    }

    private async Task<int> PollScopedAsync(CancellationToken ct)
    {
        var ids = await db.Printers.Where(p => p.SiteId != null && scope.SiteIds.Contains(p.SiteId.Value))
            .Select(p => p.Id).ToListAsync(ct);
        var n = 0;
        foreach (var id in ids)
            if (await poller.PollOneAsync(id, User.Identity?.Name ?? "admin", ct) is null) n++;
        return n;
    }

    private async Task LoadAsync()
    {
        await scope.LoadAsync();
        CanWrite = scope.CanWrite;

        var printersQ = db.Printers.AsQueryable();
        if (!scope.IsSuperAdmin)
            printersQ = printersQ.Where(p => p.SiteId != null && scope.SiteIds.Contains(p.SiteId.Value));
        var printers = await printersQ.OrderBy(p => p.WorkstationName).ThenBy(p => p.Name).ToListAsync();
        var cfgs = await db.PrinterMeterConfigs.ToDictionaryAsync(c => c.PrinterId);
        var lasts = (await db.MeterReadings
                .GroupBy(m => m.PrinterId)
                .Select(g => g.OrderByDescending(x => x.TakenAt).First())
                .ToListAsync())
            .ToDictionary(m => m.PrinterId);

        Rows = printers.Select(p => new Row(
            p, cfgs.GetValueOrDefault(p.Id), lasts.GetValueOrDefault(p.Id))).ToList();
    }
}
