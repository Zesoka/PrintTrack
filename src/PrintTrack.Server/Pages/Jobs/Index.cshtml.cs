using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;
using PrintTrack.Shared;

namespace PrintTrack.Server.Pages.Jobs;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public const int PageSize = 50;
    private const int ExportCap = 100_000;

    [BindProperty(SupportsGet = true, Name = "user")] public string? UserFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? Printer { get; set; }
    [BindProperty(SupportsGet = true)] public PrintJobStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int? SiteId { get; set; }
    [BindProperty(SupportsGet = true)] public int? DepartmentId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;

    public SelectList Sites { get; private set; } = new(Array.Empty<string>());
    public SelectList Departments { get; private set; } = new(Array.Empty<string>());

    public List<PrintJobRecord> Jobs { get; private set; } = [];
    public int Total { get; private set; }
    public int TotalSheets { get; private set; }
    public int Pages => (int)Math.Ceiling(Total / (double)PageSize);

    private IQueryable<PrintJobRecord> Filtered()
    {
        var q = db.PrintJobs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(UserFilter))
            q = q.Where(j => j.UserNameRaw.ToUpper().Contains(UserFilter.ToUpper()));
        if (!string.IsNullOrWhiteSpace(Printer))
            q = q.Where(j => j.PrinterNameRaw.ToUpper().Contains(Printer.ToUpper()));
        if (Status is not null)
            q = q.Where(j => j.Status == Status);
        if (SiteId is int sid)
            q = q.Where(j => j.SiteId == sid);
        if (DepartmentId is int did)
            q = q.Where(j => j.EndUser.DepartmentId == did);
        if (From is DateOnly f)
            q = q.Where(j => j.SubmittedAt >= new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        if (To is DateOnly t)
            q = q.Where(j => j.SubmittedAt < new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        return q;
    }

    public async Task OnGetAsync()
    {
        await LoadFiltersAsync();
        var q = Filtered();
        Total = await q.CountAsync();
        TotalSheets = await q.Where(j => j.Status == PrintJobStatus.Printed).SumAsync(j => (int?)j.Sheets) ?? 0;
        if (P < 1) P = 1;
        Jobs = await q.Include(j => j.Site).Include(j => j.EndUser).ThenInclude(u => u.Department)
            .OrderByDescending(j => j.SubmittedAt)
            .Skip((P - 1) * PageSize).Take(PageSize).ToListAsync();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
    {
        var rows = await Filtered()
            .Include(j => j.Site).Include(j => j.EndUser).ThenInclude(u => u.Department)
            .OrderByDescending(j => j.SubmittedAt).Take(ExportCap).ToListAsync(ct);

        var csv = new CsvBuilder();
        csv.Row("Enviado", "Completado", "Sede", "Área", "Usuario", "Documento", "Impresora", "Equipo",
            "Páginas", "Copias", "Hojas", "Color", "Dúplex", "Tamaño", "Estado", "Mensaje");

        foreach (var j in rows)
            csv.Row(j.SubmittedAt, j.CompletedAt, j.Site?.Name, j.EndUser.Department?.Name,
                j.UserNameRaw, j.DocumentName, j.PrinterNameRaw, j.WorkstationName,
                j.Pages, j.Copies, j.Sheets,
                j.Color switch { ColorMode.Color => "Color", ColorMode.Grayscale => "BN", _ => "" },
                j.Duplex switch { DuplexMode.Duplex => "Dúplex", DuplexMode.Simplex => "Simple", _ => "" },
                j.PaperSize, Ui.StatusText(j.Status), j.DecisionMessage);

        return File(csv.ToBytes(), "text/csv", $"trabajos_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }

    private async Task LoadFiltersAsync()
    {
        Sites = new SelectList(
            await db.Sites.OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync(), "Id", "Name");
        Departments = new SelectList(
            await db.Departments.OrderBy(d => d.Name).Select(d => new { d.Id, d.Name }).ToListAsync(), "Id", "Name");
    }
}
