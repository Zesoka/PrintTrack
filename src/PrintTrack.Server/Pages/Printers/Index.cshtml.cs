using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;
using PrintTrack.Shared;

namespace PrintTrack.Server.Pages.Printers;

public sealed class IndexModel(AppDbContext db, AdminScope scope) : PageModel
{
    public bool CanWrite { get; private set; }
    public sealed record Row(Printer Printer, int Sheets30, int Jobs30);
    public List<Row> Rows { get; private set; } = [];

    [BindProperty] public NewPrinter Create { get; set; } = new();
    public List<Site> AvailableSites { get; private set; } = [];

    public sealed class NewPrinter
    {
        [Required] public string Name { get; set; } = "";
        public string? Location { get; set; }
        public int? SiteId { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>Registers a printer by hand — rarely needed now that Descubrir hace la carga masiva.</summary>
    public async Task<IActionResult> OnPostCreateAsync()
    {
        await scope.LoadAsync();
        if (!scope.CanWrite) return Forbid();
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var name = Create.Name.Trim();
        if (await db.Printers.AnyAsync(p => p.Name == name && p.WorkstationName == null))
        {
            ModelState.AddModelError("Create.Name", "Ya existe una impresora con ese nombre.");
            await LoadAsync();
            return Page();
        }
        // A Sede-scoped admin can only file the new printer under one of their own Sedes.
        var siteId = Create.SiteId;
        if (!scope.IsSuperAdmin && (siteId is null || !scope.SiteIds.Contains(siteId.Value)))
        {
            ModelState.AddModelError("Create.SiteId", "Elegí una de tus Sedes.");
            await LoadAsync();
            return Page();
        }

        db.Printers.Add(new Printer { Name = name, WorkstationName = null, Location = Create.Location, SiteId = siteId });
        await db.SaveChangesAsync();
        TempData["Msg"] = "Impresora creada.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await scope.LoadAsync();
        if (!scope.CanWrite) return Forbid();

        var printer = await db.Printers.FindAsync(id);
        if (printer is null) return NotFound();
        if (!scope.CanSeeSite(printer.SiteId)) return Forbid();

        if (await db.PrintJobs.AnyAsync(j => j.PrinterId == id))
        {
            TempData["Err"] = "No se puede borrar: tiene trabajos registrados. Deshabilitala en su lugar.";
            return RedirectToPage();
        }

        db.Printers.Remove(printer);
        await db.SaveChangesAsync();
        TempData["Msg"] = "Impresora eliminada.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        await scope.LoadAsync();
        CanWrite = scope.CanWrite;

        var sitesQ = db.Sites.AsQueryable();
        if (!scope.IsSuperAdmin) sitesQ = sitesQ.Where(s => scope.SiteIds.Contains(s.Id));
        AvailableSites = await sitesQ.OrderBy(s => s.Name).ToListAsync();

        var printersQ = db.Printers.AsQueryable();
        if (!scope.IsSuperAdmin)
            printersQ = printersQ.Where(p => p.SiteId != null && scope.SiteIds.Contains(p.SiteId.Value));

        var printers = await printersQ
            .OrderBy(p => p.WorkstationName).ThenBy(p => p.Name).ToListAsync();

        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var stats = await db.PrintJobs
            .Where(j => j.Status == PrintJobStatus.Printed && j.SubmittedAt >= since)
            .GroupBy(j => j.PrinterId)
            .Select(g => new { g.Key, Sheets = g.Sum(x => x.Sheets), Jobs = g.Count() })
            .ToDictionaryAsync(x => x.Key);

        Rows = printers.Select(p =>
        {
            stats.TryGetValue(p.Id, out var s);
            return new Row(p, s?.Sheets ?? 0, s?.Jobs ?? 0);
        }).ToList();
    }
}
