using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;
using PrintTrack.Shared;

namespace PrintTrack.Server.Pages;

public sealed class IndexModel(AppDbContext db, AdminScope scope) : PageModel
{
    public int UsersCount { get; private set; }
    public int PrintersOnline { get; private set; }
    public int JobsToday { get; private set; }
    public int SheetsToday { get; private set; }
    public int ColorSheetsToday { get; private set; }
    public int DeniedToday { get; private set; }

    public List<PrintJobRecord> Recent { get; private set; } = [];
    public List<(string Name, int Sheets, int Jobs)> TopUsers { get; private set; } = [];
    public List<(string Name, int Sheets, int Jobs)> TopPrinters { get; private set; } = [];
    public List<(string Name, int Sheets, int Jobs)> BySite { get; private set; } = [];
    public List<(string Name, int Sheets, int Jobs)> ByDepartment { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await scope.LoadAsync();

        // Explicit UTC offset — Npgsql refuses to write a DateTimeOffset with any other offset to
        // timestamptz, and DateTimeOffset.UtcNow.Date implicitly reconverts through the *local*
        // time zone (broken now that the container runs with TZ set instead of defaulting to UTC).
        var since = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var week = DateTimeOffset.UtcNow.AddDays(-7);
        var dayAgo = DateTimeOffset.UtcNow.AddDays(-1);

        var jobsScoped = db.PrintJobs.Where(j => scope.IsSuperAdmin || (j.SiteId != null && scope.SiteIds.Contains(j.SiteId.Value)));
        var printersScoped = db.Printers.Where(p => scope.IsSuperAdmin || (p.SiteId != null && scope.SiteIds.Contains(p.SiteId.Value)));

        UsersCount = scope.IsSuperAdmin
            ? await db.EndUsers.CountAsync()
            : await jobsScoped.Select(j => j.EndUserId).Distinct().CountAsync();
        PrintersOnline = await printersScoped.CountAsync(p => p.LastSeenAt >= dayAgo);

        var todayPrinted = jobsScoped.Where(j => j.SubmittedAt >= since && j.Status == PrintJobStatus.Printed);
        JobsToday = await todayPrinted.CountAsync();
        SheetsToday = await todayPrinted.SumAsync(j => (int?)j.Sheets) ?? 0;
        ColorSheetsToday = await todayPrinted.Where(j => j.Color == ColorMode.Color).SumAsync(j => (int?)j.Sheets) ?? 0;
        DeniedToday = await jobsScoped.CountAsync(j => j.SubmittedAt >= since && j.Status == PrintJobStatus.Denied);

        Recent = await jobsScoped
            .Include(j => j.Site).Include(j => j.EndUser).ThenInclude(u => u.Department)
            .OrderByDescending(j => j.SubmittedAt).Take(15).ToListAsync();

        TopUsers = (await todayPrinted
                .GroupBy(j => j.UserNameRaw)
                .Select(g => new { Name = g.Key, Sheets = g.Sum(x => x.Sheets), Jobs = g.Count() })
                .OrderByDescending(x => x.Jobs).Take(8).ToListAsync())
            .Select(x => (x.Name, x.Sheets, x.Jobs)).ToList();

        TopPrinters = (await todayPrinted
                .GroupBy(j => j.PrinterNameRaw)
                .Select(g => new { Name = g.Key, Sheets = g.Sum(x => x.Sheets), Jobs = g.Count() })
                .OrderByDescending(x => x.Jobs).Take(8).ToListAsync())
            .Select(x => (x.Name, x.Sheets, x.Jobs)).ToList();

        var weekPrinted = jobsScoped.Where(j => j.SubmittedAt >= week && j.Status == PrintJobStatus.Printed);

        BySite = (await weekPrinted
                .GroupBy(j => j.Site!.Name)
                .Select(g => new { Name = g.Key, Sheets = g.Sum(x => x.Sheets), Jobs = g.Count() })
                .OrderByDescending(x => x.Jobs).Take(12).ToListAsync())
            .Select(x => (Name: x.Name ?? "(sin sede)", x.Sheets, x.Jobs)).ToList();

        ByDepartment = (await weekPrinted
                .GroupBy(j => j.EndUser.Department!.Name)
                .Select(g => new { Name = g.Key, Sheets = g.Sum(x => x.Sheets), Jobs = g.Count() })
                .OrderByDescending(x => x.Jobs).Take(12).ToListAsync())
            .Select(x => (Name: x.Name ?? "(sin área)", x.Sheets, x.Jobs)).ToList();
    }
}
