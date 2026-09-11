using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintTrack.Server.Data;
using PrintTrack.Server.Options;

namespace PrintTrack.Server.Services;

/// <summary>
/// Polls every SNMP-enabled printer on a schedule and stores a <see cref="MeterReading"/>.
/// Also exposes on-demand polling for the "Sondear ahora" buttons.
/// </summary>
public sealed class MeterPollingService(
    IServiceProvider sp,
    SnmpMeterReader reader,
    IOptions<PrintTrackOptions> options,
    ILogger<MeterPollingService> logger) : BackgroundService
{
    private readonly MeterOptions _opt = options.Value.Meter;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled)
        {
            logger.LogInformation("Sondeo SNMP deshabilitado (PrintTrack:Meter:Enabled=false).");
            return;
        }

        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, _opt.PollHours)));
        do
        {
            try { await PollAllAsync("scheduler", ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Ciclo de sondeo SNMP falló."); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task<int> PollAllAsync(string triggeredBy, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.PrinterMeterConfigs
            .Where(c => c.Enabled && c.Host != null && c.Host != "")
            .Select(c => c.PrinterId).ToListAsync(ct);
        if (ids.Count == 0) return 0;

        var stored = new int[1];
        using var gate = new SemaphoreSlim(Math.Max(1, _opt.MaxParallelism));
        await Task.WhenAll(ids.Select(async id =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (await PollOneAsync(id, triggeredBy, ct) is null)
                    Interlocked.Increment(ref stored[0]);
            }
            finally { gate.Release(); }
        }));

        logger.LogInformation("Sondeo SNMP ({By}): {Stored}/{Total} lecturas guardadas.", triggeredBy, stored[0], ids.Count);
        return stored[0];
    }

    /// <summary>Poll one printer now. Returns the error string, or null on success.</summary>
    public async Task<string?> PollOneAsync(int printerId, string triggeredBy, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cfg = await db.PrinterMeterConfigs.FirstOrDefaultAsync(c => c.PrinterId == printerId, ct);
        if (cfg is null) return "La impresora no tiene configuración SNMP.";

        var sample = await reader.ReadAsync(cfg, _opt.TimeoutMs, ct);

        cfg.LastPolledAt = DateTimeOffset.UtcNow;
        cfg.LastError = sample.Ok ? null : sample.Error;
        if (sample.DeviceName is { Length: > 0 }) cfg.DeviceName = sample.DeviceName;
        if (sample.DeviceDescr is { Length: > 0 }) cfg.DeviceDescr = Truncate(sample.DeviceDescr, 400);

        if (sample.Ok)
        {
            db.MeterReadings.Add(new MeterReading
            {
                PrinterId = printerId,
                TakenAt = DateTimeOffset.UtcNow,
                Source = MeterSource.Snmp,
                Total = sample.Total!.Value,
                Mono = sample.Mono,
                Color = sample.Color,
                CreatedBy = triggeredBy
            });
        }

        await db.SaveChangesAsync(ct);
        return sample.Error;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
