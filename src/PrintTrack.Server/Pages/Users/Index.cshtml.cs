using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Shared;

namespace PrintTrack.Server.Pages.Users;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public sealed record Row(EndUser User, int Sheets30, int Jobs30);
    public List<Row> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var query = db.EndUsers.Include(u => u.Department).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var t = Q.Trim().ToUpperInvariant();
            query = query.Where(u => u.NormalizedUserName.Contains(t)
                || (u.FullName != null && u.FullName.ToUpper().Contains(t)));
        }
        var users = await query.OrderBy(u => u.NormalizedUserName).Take(500).ToListAsync();

        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var stats = await db.PrintJobs
            .Where(j => j.Status == PrintJobStatus.Printed && j.SubmittedAt >= since)
            .GroupBy(j => j.EndUserId)
            .Select(g => new { g.Key, Sheets = g.Sum(x => x.Sheets), Jobs = g.Count() })
            .ToDictionaryAsync(x => x.Key);

        Rows = users.Select(u =>
        {
            stats.TryGetValue(u.Id, out var s);
            return new Row(u, s?.Sheets ?? 0, s?.Jobs ?? 0);
        }).ToList();
    }
}
