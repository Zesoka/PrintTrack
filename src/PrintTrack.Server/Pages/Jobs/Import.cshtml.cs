using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.Jobs;

public sealed class ImportModel(AppDbContext db, HpJobLogImporter importer) : PageModel
{
    public SelectList Printers { get; private set; } = new(Array.Empty<string>());
    public ImportResult? Result { get; private set; }

    [BindProperty] public int PrinterId { get; set; }
    [BindProperty] public bool OnlyPrintJobs { get; set; } = true;
    [BindProperty] public IFormFile? Upload { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadAsync();

        if (PrinterId == 0) { ModelState.AddModelError(nameof(PrinterId), "Elegí la impresora."); return Page(); }
        if (Upload is null || Upload.Length == 0) { ModelState.AddModelError(nameof(Upload), "Subí el archivo exportado."); return Page(); }
        if (Upload.Length > 20 * 1024 * 1024) { ModelState.AddModelError(nameof(Upload), "Archivo demasiado grande (máx 20 MB)."); return Page(); }

        string content;
        using (var reader = new StreamReader(Upload.OpenReadStream()))
            content = await reader.ReadToEndAsync(ct);

        Result = await importer.ImportAsync(PrinterId, content, OnlyPrintJobs, User.Identity?.Name ?? "admin", ct);
        if (Result.Ok)
            TempData["Msg"] = $"Importación: {Result.Imported} nuevos, {Result.Duplicates} duplicados, "
                            + $"{Result.SkippedType} no-impresión, {Result.Errors} con error.";
        return Page();
    }

    private async Task LoadAsync() =>
        Printers = new SelectList(
            await db.Printers.OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(),
            "Id", "Name");
}
