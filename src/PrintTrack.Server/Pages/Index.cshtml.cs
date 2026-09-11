using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrintTrack.Server.Data;
using PrintTrack.Shared;

namespace PrintTrack.Server.Pages;

public sealed class IndexModel(AppDbContext db) : PageModel
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
        var since = DateTimeOffset.UtcNow.Date;
        var week = DateTimeOffset.UtcNow.AddDays(-7);
        var dayAgo = DateTimeOffset.UtcNow.AddDays(-1);

        UsersCount = await db.EndUsers.CountAsync();
        PrintersOnline = await db.Printers.CountAsync(p => p.LastSeenAt >= dayAgo);

        var todayPrinted = db.PrintJobs.Where(j => j.SubmittedAt >= since && j.Status == PrintJobStatus.Printed);
        JobsToday = await todayPrinted.CountAsync();
        SheetsToday = await todayPrinted.SumAsync(j => (int?)j.Sheets) ?? 0;
        ColorSheetsToday = await todayPrinted.Where(j => j.Color == ColorMode.Color).SumAsync(j => (int?)j.Sheets) ?? 0;
        DeniedToday = await db.PrintJobs.CountAsync(j => j.SubmittedAt >= since && j.Status == PrintJobStatus.Denied);

        Recent = await db.PrintJobs
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

        var weekPrinted = db.PrintJobs.Where(j => j.SubmittedAt >= week && j.Status == PrintJobStatus.Printed);

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
