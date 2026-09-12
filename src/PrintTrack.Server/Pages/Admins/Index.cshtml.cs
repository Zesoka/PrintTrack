using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Pages.Admins;

/// <summary>Manage dashboard admins: role (SuperAdmin/SedeAdmin/SedeViewer) and, for the two
/// non-super roles, which Sedes they can see/manage. SuperAdmin-only page.</summary>
[Authorize(Roles = AdminRoles.SuperAdmin)]
public sealed class IndexModel(UserManager<AdminUser> users, AppDbContext db) : PageModel
{
    public sealed record Row(string Id, string UserName, string? Email, string Role, string SitesText);
    public List<Row> Rows { get; private set; } = [];
    public List<Site> Sites { get; private set; } = [];

    [BindProperty] public string NewUserName { get; set; } = "";
    [BindProperty] public string? NewEmail { get; set; }
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string NewRole { get; set; } = AdminRoles.SedeViewer;
    [BindProperty] public List<int> NewSiteIds { get; set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await LoadAsync();
        if (string.IsNullOrWhiteSpace(NewUserName) || string.IsNullOrWhiteSpace(NewPassword))
        {
            ModelState.AddModelError(string.Empty, "Usuario y contraseña son obligatorios.");
            return Page();
        }

        var user = new AdminUser
        {
            UserName = NewUserName.Trim(),
            Email = string.IsNullOrWhiteSpace(NewEmail) ? null : NewEmail.Trim(),
            EmailConfirmed = true
        };
        var res = await users.CreateAsync(user, NewPassword);
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }

        await users.AddToRoleAsync(user, NewRole);
        if (NewRole != AdminRoles.SuperAdmin)
            foreach (var siteId in NewSiteIds.Distinct())
                db.AdminSiteAccess.Add(new AdminSiteAccess { AdminUserId = user.Id, SiteId = siteId });
        await db.SaveChangesAsync();

        TempData["Msg"] = $"Administrador '{user.UserName}' creado.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is not null)
        {
            if (string.Equals(user.UserName, User.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Err"] = "No podés borrar tu propio usuario.";
                return RedirectToPage();
            }
            await users.DeleteAsync(user);
            TempData["Msg"] = $"Administrador '{user.UserName}' eliminado.";
        }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Sites = await db.Sites.OrderBy(s => s.Name).ToListAsync();
        var access = await db.AdminSiteAccess.Include(a => a.Site).ToListAsync();

        var rows = new List<Row>();
        foreach (var u in users.Users.OrderBy(u => u.UserName).ToList())
        {
            var roles = await users.GetRolesAsync(u);
            var role = roles.FirstOrDefault() ?? "(sin rol)";
            var sitesText = role == AdminRoles.SuperAdmin
                ? "todas"
                : string.Join(", ", access.Where(a => a.AdminUserId == u.Id).Select(a => a.Site.Name));
            rows.Add(new Row(u.Id, u.UserName ?? "", u.Email, role, sitesText));
        }
        Rows = rows;
    }
}
