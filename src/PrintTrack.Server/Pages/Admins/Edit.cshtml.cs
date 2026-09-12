using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Pages.Admins;

[Authorize(Roles = AdminRoles.SuperAdmin)]
public sealed class EditModel(UserManager<AdminUser> users, AppDbContext db) : PageModel
{
    public AdminUser Target { get; private set; } = null!;
    public List<Site> Sites { get; private set; } = [];

    [BindProperty] public string Role { get; set; } = AdminRoles.SedeViewer;
    [BindProperty] public List<int> SiteIds { get; set; } = [];
    [BindProperty] public string? NewPassword { get; set; }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        Target = user;
        Sites = await db.Sites.OrderBy(s => s.Name).ToListAsync();

        var roles = await users.GetRolesAsync(user);
        Role = roles.FirstOrDefault() ?? AdminRoles.SedeViewer;
        SiteIds = await db.AdminSiteAccess.Where(a => a.AdminUserId == id).Select(a => a.SiteId).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        Target = user;
        Sites = await db.Sites.OrderBy(s => s.Name).ToListAsync();

        var currentRoles = await users.GetRolesAsync(user);
        if (currentRoles.Count > 0) await users.RemoveFromRolesAsync(user, currentRoles);
        await users.AddToRoleAsync(user, Role);

        var existing = await db.AdminSiteAccess.Where(a => a.AdminUserId == id).ToListAsync();
        db.AdminSiteAccess.RemoveRange(existing);
        if (Role != AdminRoles.SuperAdmin)
            foreach (var siteId in SiteIds.Distinct())
                db.AdminSiteAccess.Add(new AdminSiteAccess { AdminUserId = id, SiteId = siteId });
        await db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(NewPassword))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var res = await users.ResetPasswordAsync(user, token, NewPassword);
            if (!res.Succeeded)
            {
                foreach (var e in res.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return Page();
            }
        }

        TempData["Msg"] = $"'{user.UserName}' actualizado.";
        return RedirectToPage("Index");
    }
}
