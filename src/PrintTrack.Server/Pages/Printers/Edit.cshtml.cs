using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Pages.Printers;

public sealed class EditModel(AppDbContext db) : PageModel
{
    public SelectList Sites { get; private set; } = new(Array.Empty<string>());
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Location { get; set; }
        public int? SiteId { get; set; }
        public bool IsTracked { get; set; } = true;
        public bool IsDisabled { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var p = await db.Printers.FindAsync(id);
        if (p is null) return NotFound();
        Input = new InputModel
        {
            Id = p.Id, Name = p.Name, Location = p.Location, SiteId = p.SiteId,
            IsTracked = p.IsTracked, IsDisabled = p.IsDisabled
        };
        await LoadSitesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var p = await db.Printers.FindAsync(Input.Id);
        if (p is null) return NotFound();
        await LoadSitesAsync();

        p.Location = Input.Location;
        p.SiteId = Input.SiteId;
        p.IsTracked = Input.IsTracked;
        p.IsDisabled = Input.IsDisabled;
        await db.SaveChangesAsync();

        TempData["Msg"] = "Impresora actualizada.";
        return RedirectToPage("Index");
    }

    private async Task LoadSitesAsync() =>
        Sites = new SelectList(
            await db.Sites.Where(s => s.IsActive).OrderBy(s => s.Name)
                .Select(s => new { s.Id, s.Name }).ToListAsync(),
            "Id", "Name");
}
