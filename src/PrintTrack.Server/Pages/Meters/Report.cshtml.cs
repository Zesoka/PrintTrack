using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.Meters;

public sealed class ReportModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }

    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }

    public sealed record Row(
        string Printer, string? Host, string? DeviceName,
        DateTimeOffset? BaselineAt, DateTimeOffset? EndAt, bool Partial,
        long DeltaTotal, long? DeltaMono, long? DeltaColor, bool Backwards);

    public List<Row> Rows { get; private set; } = [];

    public long TotTotal => Rows.Sum(r => r.DeltaTotal);
    public long TotMono => Rows.Sum(r => r.DeltaMono ?? 0);
    public long TotColor => Rows.Sum(r => r.DeltaColor ?? 0);

    public async Task OnGetAsync()
    {
        PeriodEnd = To ?? DateOnly.FromDateTime(DateTime.Today);
        PeriodStart = From ?? PeriodEnd.AddDays(-30);
        Rows = await BuildAsync();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
    {
        PeriodEnd = To ?? DateOnly.FromDateTime(DateTime.Today);
        PeriodStart = From ?? PeriodEnd.AddDays(-30);
        var rows = await BuildAsync();

        var csv = new CsvBuilder();
        csv.Row("Impresora", "Host", "Hostname SNMP", "Lectura base", "Lectura final",
            "Parcial", "Δ total", "Δ B/N", "Δ color", "Revisar");
        foreach (var r in rows)
            csv.Row(r.Printer, r.Host, r.DeviceName, r.BaselineAt, r.EndAt, r.Partial,
                r.DeltaTotal, r.DeltaMono, r.DeltaColor, r.Backwards);

        return File(csv.ToBytes(), "text/csv",
            $"contadores_{PeriodStart:yyyyMMdd}_{PeriodEnd:yyyyMMdd}.csv");
    }

    private async Task<List<Row>> BuildAsync()
    {
        var startUtc = new DateTimeOffset(PeriodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endUtc = new DateTimeOffset(PeriodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var printers = await db.Printers.OrderBy(p => p.Name).ToListAsync();
        var cfgs = await db.PrinterMeterConfigs.ToDictionaryAsync(c => c.PrinterId);
        var rows = new List<Row>();

        foreach (var p in printers)
        {
            var baseline = await db.MeterReadings
                .Where(m => m.PrinterId == p.Id && m.TakenAt <= startUtc)
                .OrderByDescending(m => m.TakenAt).FirstOrDefaultAsync();
            var partial = false;
            if (baseline is null)
            {
                baseline = await db.MeterReadings
                    .Where(m => m.PrinterId == p.Id && m.TakenAt < endUtc)
                    .OrderBy(m => m.TakenAt).FirstOrDefaultAsync();
                partial = baseline is not null;
            }
            var end = await db.MeterReadings
                .Where(m => m.PrinterId == p.Id && m.TakenAt < endUtc)
                .OrderByDescending(m => m.TakenAt).FirstOrDefaultAsync();

            if (baseline is null || end is null || end.Id == baseline.Id) continue;

            var dt = end.Total - baseline.Total;
            long? dm = end.Mono is long em && baseline.Mono is long bm ? em - bm : null;
            long? dc = end.Color is long ec && baseline.Color is long bc ? ec - bc : null;
            var back = dt < 0 || dm < 0 || dc < 0;

            cfgs.TryGetValue(p.Id, out var cfg);
            rows.Add(new Row(p.Name, cfg?.Host, cfg?.DeviceName,
                baseline.TakenAt, end.TakenAt, partial,
                Math.Max(0, dt), dm is null ? null : Math.Max(0, dm.Value),
                dc is null ? null : Math.Max(0, dc.Value), back));
        }
        return rows;
    }
}
