using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.Printers;

public sealed class EditModel(AppDbContext db, AdminScope scope) : PageModel
{
    public SelectList Sites { get; private set; } = new(Array.Empty<string>());
    public bool CanWrite { get; private set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Location { get; set; }
        public int? SiteId { get; set; }
        public bool IsTracked { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await scope.LoadAsync();
        var p = await db.Printers.FindAsync(id);
        if (p is null) return NotFound();
        if (!scope.CanSeeSite(p.SiteId)) return Forbid();
        CanWrite = scope.CanWrite;

        Input = new InputModel
        {
            Id = p.Id, Name = p.Name, Location = p.Location, SiteId = p.SiteId,
            IsTracked = p.IsTracked
        };
        await LoadSitesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await scope.LoadAsync();
        var p = await db.Printers.FindAsync(Input.Id);
        if (p is null) return NotFound();
        if (!scope.CanSeeSite(p.SiteId)) return Forbid();
        if (!scope.CanWrite) return Forbid();
        // A Sede-scoped admin can't move the printer out of their own Sedes.
        if (!scope.IsSuperAdmin && (Input.SiteId is null || !scope.SiteIds.Contains(Input.SiteId.Value)))
        {
            ModelState.AddModelError("Input.SiteId", "Elegí una de tus Sedes.");
            CanWrite = scope.CanWrite;
            await LoadSitesAsync();
            return Page();
        }
        await LoadSitesAsync();

        p.Location = Input.Location;
        p.SiteId = Input.SiteId;
        p.IsTracked = Input.IsTracked;
        await db.SaveChangesAsync();

        TempData["Msg"] = "Impresora actualizada.";
        return RedirectToPage("Index");
    }

    private async Task LoadSitesAsync()
    {
        var q = db.Sites.Where(s => s.IsActive).AsQueryable();
        if (!scope.IsSuperAdmin) q = q.Where(s => scope.SiteIds.Contains(s.Id));
        Sites = new SelectList(await q.OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync(), "Id", "Name");
    }
}
