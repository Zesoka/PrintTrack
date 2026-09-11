using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.Meters;

public sealed class IndexModel(AppDbContext db, MeterPollingService poller) : PageModel
{
    public sealed record Row(Printer Printer, PrinterMeterConfig? Cfg, MeterReading? Last);
    public List<Row> Rows { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostPollAllAsync(CancellationToken ct)
    {
        var n = await poller.PollAllAsync(User.Identity?.Name ?? "admin", ct);
        TempData["Msg"] = $"Sondeo completado: {n} lectura(s) nueva(s).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPollOneAsync(int id, CancellationToken ct)
    {
        var err = await poller.PollOneAsync(id, User.Identity?.Name ?? "admin", ct);
        if (err is null) TempData["Msg"] = "Lectura SNMP guardada.";
        else TempData["Err"] = $"SNMP: {err}";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var printers = await db.Printers.OrderBy(p => p.WorkstationName).ThenBy(p => p.Name).ToListAsync();
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
