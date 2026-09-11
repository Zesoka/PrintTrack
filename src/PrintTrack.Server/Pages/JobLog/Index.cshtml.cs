using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.JobLog;

public sealed class IndexModel(AppDbContext db, JobLogPollingService poller) : PageModel
{
    public sealed record Row(Printer Printer, PrinterJobLogConfig? Cfg);
    public List<Row> Rows { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostPollAllAsync(CancellationToken ct)
    {
        var n = await poller.PollAllAsync(User.Identity?.Name ?? "admin", ct);
        TempData["Msg"] = $"Pull completado: {n} trabajo(s) nuevo(s).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPollOneAsync(int id, CancellationToken ct)
    {
        var (n, err) = await poller.PollOneAsync(id, User.Identity?.Name ?? "admin", ct);
        // "Job Log no disponible ... importado por IPP: N" is IPP doing its job, not a failure —
        // only treat it as an error banner when neither path produced anything.
        if (err is null || err.Contains("importado por IPP:")) TempData["Msg"] = $"Traído: {n} trabajo(s) nuevo(s). {err}".Trim();
        else TempData["Err"] = err;
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var printers = await db.Printers.OrderBy(p => p.Name).ToListAsync();
        var cfgs = await db.PrinterJobLogConfigs.ToDictionaryAsync(c => c.PrinterId);
        Rows = printers.Select(p => new Row(p, cfgs.GetValueOrDefault(p.Id))).ToList();
    }
}
