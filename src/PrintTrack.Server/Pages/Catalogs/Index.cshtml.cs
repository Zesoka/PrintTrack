using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Pages.Catalogs;

[Authorize(Roles = AdminRoles.SuperAdmin)]
public sealed class IndexModel(AppDbContext db) : PageModel
{
    public List<Site> Sites { get; private set; } = [];
    public List<Department> Departments { get; private set; } = [];

    [BindProperty] public Entry NewSite { get; set; } = new();
    [BindProperty] public Entry NewDept { get; set; } = new();

    public sealed class Entry
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddSiteAsync()
    {
        var name = NewSite.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            TempData["Err"] = "El nombre de la sede es obligatorio.";
        else if (await db.Sites.AnyAsync(s => s.Name == name))
            TempData["Err"] = "Ya existe una sede con ese nombre.";
        else
        {
            db.Sites.Add(new Site { Name = name, Code = Trim(NewSite.Code) });
            await db.SaveChangesAsync();
            TempData["Msg"] = "Sede agregada.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddDeptAsync()
    {
        var name = NewDept.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            TempData["Err"] = "El nombre del área es obligatorio.";
        else if (await db.Departments.AnyAsync(d => d.Name == name))
            TempData["Err"] = "Ya existe un área con ese nombre.";
        else
        {
            db.Departments.Add(new Department { Name = name, Code = Trim(NewDept.Code) });
            await db.SaveChangesAsync();
            TempData["Msg"] = "Área agregada.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteSiteAsync(int id)
    {
        var s = await db.Sites.FindAsync(id);
        if (s is not null) { db.Sites.Remove(s); await db.SaveChangesAsync(); TempData["Msg"] = "Sede eliminada."; }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteDeptAsync(int id)
    {
        var d = await db.Departments.FindAsync(id);
        if (d is not null) { db.Departments.Remove(d); await db.SaveChangesAsync(); TempData["Msg"] = "Área eliminada."; }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Sites = await db.Sites.OrderBy(s => s.Name).ToListAsync();
        Departments = await db.Departments.Include(d => d.Users).OrderBy(d => d.Name).ToListAsync();
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
