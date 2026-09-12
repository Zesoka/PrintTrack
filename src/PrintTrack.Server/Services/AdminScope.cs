using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Services;

/// <summary>
/// Resolves what the currently logged-in admin is allowed to see: <see cref="AdminRoles.SuperAdmin"/>
/// sees every Sede; <see cref="AdminRoles.SedeAdmin"/>/<see cref="AdminRoles.SedeViewer"/> are
/// limited to their assigned Sedes (<see cref="Data.AdminSiteAccess"/>), read-only for viewers.
/// Scoped per-request — call <see cref="LoadAsync"/> once at the top of a page handler before use.
/// </summary>
public sealed class AdminScope(AppDbContext db, UserManager<AdminUser> userManager, IHttpContextAccessor http)
{
    private bool _loaded;
    public bool IsSuperAdmin { get; private set; }
    public bool CanWrite { get; private set; }
    // Concrete List<int>, not IReadOnlyList — EF Core's query translator handles a captured
    // List<T>.Contains(...) as a straightforward SQL "IN (...)" more reliably than an interface type.
    public List<int> SiteIds { get; private set; } = [];
    public string? UserId { get; private set; }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded) return;
        _loaded = true;

        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true) return;

        var user = await userManager.GetUserAsync(principal);
        if (user is null) return;
        UserId = user.Id;

        var roles = await userManager.GetRolesAsync(user);
        IsSuperAdmin = roles.Contains(AdminRoles.SuperAdmin);
        CanWrite = IsSuperAdmin || roles.Contains(AdminRoles.SedeAdmin);

        if (!IsSuperAdmin)
            SiteIds = await db.AdminSiteAccess.Where(a => a.AdminUserId == user.Id)
                .Select(a => a.SiteId).ToListAsync(ct);
    }

    /// <summary>True if this admin may see rows tagged with this SiteId (null site = visible to
    /// everyone — it just means the row predates Sede tagging or the printer has none assigned).</summary>
    public bool CanSeeSite(int? siteId) => IsSuperAdmin || siteId is null || SiteIds.Contains(siteId.Value);
}
