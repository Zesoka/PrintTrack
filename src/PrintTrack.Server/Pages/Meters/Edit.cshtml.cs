using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.Meters;

public sealed class EditModel(AppDbContext db, MeterPollingService poller, SnmpMeterReader reader) : PageModel
{
    public Printer Printer { get; private set; } = null!;
    public PrinterMeterConfig? Cfg { get; private set; }
    public List<MeterReading> Readings { get; private set; } = [];
    public string? OidTestResult { get; private set; }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public ManualReading Manual { get; set; } = new();
    [BindProperty] public string? TestOid { get; set; }

    public sealed class InputModel
    {
        public int PrinterId { get; set; }
        public bool Enabled { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; } = 161;
        public SnmpVersion Version { get; set; } = SnmpVersion.V2c;
        public string Community { get; set; } = "public";
        public string? SecurityName { get; set; }
        public string? AuthProtocol { get; set; }
        public string? AuthPassword { get; set; }
        public string? PrivProtocol { get; set; }
        public string? PrivPassword { get; set; }
        [Required] public string OidTotal { get; set; } = "1.3.6.1.2.1.43.10.2.1.4.1.1";
        public string? OidMono { get; set; }
        public string? OidColor { get; set; }
    }

    public sealed class ManualReading
    {
        public long Total { get; set; }
        public long? Mono { get; set; }
        public long? Color { get; set; }
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public string? Note { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        Input = Cfg is null
            ? new InputModel { PrinterId = id }
            : new InputModel
            {
                PrinterId = id, Enabled = Cfg.Enabled, Host = Cfg.Host, Port = Cfg.Port,
                Version = Cfg.Version, Community = Cfg.Community, SecurityName = Cfg.SecurityName,
                AuthProtocol = Cfg.AuthProtocol, AuthPassword = Cfg.AuthPassword,
                PrivProtocol = Cfg.PrivProtocol, PrivPassword = Cfg.PrivPassword,
                OidTotal = Cfg.OidTotal, OidMono = Cfg.OidMono, OidColor = Cfg.OidColor
            };
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!await LoadAsync(Input.PrinterId)) return NotFound();
        if (!await SaveConfigAsync(requireHost: false)) return Page();

        TempData["Msg"] = "Configuración SNMP guardada.";
        return RedirectToPage(new { id = Input.PrinterId });
    }

    /// <summary>Save the form and immediately poll — the "Sondear ahora" button.</summary>
    public async Task<IActionResult> OnPostPollAsync(CancellationToken ct)
    {
        if (!await LoadAsync(Input.PrinterId)) return NotFound();
        if (!await SaveConfigAsync(requireHost: true)) return Page();

        var err = await poller.PollOneAsync(Input.PrinterId, User.Identity?.Name ?? "admin", ct);
        if (err is null) TempData["Msg"] = "Configuración guardada y lectura SNMP obtenida.";
        else TempData["Err"] = $"Config guardada. SNMP: {err}";
        return RedirectToPage(new { id = Input.PrinterId });
    }

    private async Task<bool> SaveConfigAsync(bool requireHost)
    {
        if ((requireHost || Input.Enabled) && string.IsNullOrWhiteSpace(Input.Host))
            ModelState.AddModelError("Input.Host", "Cargá el host / IP de la impresora.");
        if (string.IsNullOrWhiteSpace(Input.OidTotal))
            ModelState.AddModelError("Input.OidTotal", "El OID de total es obligatorio.");
        if (!ModelState.IsValid) return false;

        var cfg = Cfg ?? new PrinterMeterConfig { PrinterId = Input.PrinterId };
        if (Cfg is null) { db.PrinterMeterConfigs.Add(cfg); Cfg = cfg; }

        cfg.Enabled = Input.Enabled;
        cfg.Host = Trim(Input.Host);
        cfg.Port = Input.Port <= 0 ? 161 : Input.Port;
        cfg.Version = Input.Version;
        cfg.Community = string.IsNullOrWhiteSpace(Input.Community) ? "public" : Input.Community.Trim();
        cfg.SecurityName = Trim(Input.SecurityName);
        cfg.AuthProtocol = Trim(Input.AuthProtocol);
        cfg.AuthPassword = Input.AuthPassword;
        cfg.PrivProtocol = Trim(Input.PrivProtocol);
        cfg.PrivPassword = Input.PrivPassword;
        cfg.OidTotal = Input.OidTotal.Trim();
        cfg.OidMono = Trim(Input.OidMono);
        cfg.OidColor = Trim(Input.OidColor);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<IActionResult> OnPostTestOidAsync(int id, CancellationToken ct)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Cfg is null || string.IsNullOrWhiteSpace(Cfg.Host))
            OidTestResult = "Guardá primero el host SNMP.";
        else if (string.IsNullOrWhiteSpace(TestOid))
            OidTestResult = "Ingresá un OID.";
        else
        {
            var (val, err) = await reader.GetRawAsync(Cfg, TestOid.Trim(), 4000, ct);
            OidTestResult = err is null ? $"{TestOid.Trim()}  →  {val}" : $"Error: {err}";
        }
        // rebuild Input so the form keeps its values
        Input = new InputModel
        {
            PrinterId = id, Enabled = Cfg?.Enabled ?? false, Host = Cfg?.Host, Port = Cfg?.Port ?? 161,
            Version = Cfg?.Version ?? SnmpVersion.V2c, Community = Cfg?.Community ?? "public",
            OidTotal = Cfg?.OidTotal ?? "1.3.6.1.2.1.43.10.2.1.4.1.1",
            OidMono = Cfg?.OidMono, OidColor = Cfg?.OidColor
        };
        return Page();
    }

    public async Task<IActionResult> OnPostManualAsync(int id)
    {
        var printer = await db.Printers.FindAsync(id);
        if (printer is null) return NotFound();
        db.MeterReadings.Add(new MeterReading
        {
            PrinterId = id,
            TakenAt = new DateTimeOffset(Manual.Date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            Source = MeterSource.Manual,
            Total = Manual.Total, Mono = Manual.Mono, Color = Manual.Color, Note = Manual.Note,
            CreatedBy = User.Identity?.Name ?? "admin"
        });
        await db.SaveChangesAsync();
        TempData["Msg"] = "Lectura manual agregada.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteReadingAsync(int id, long readingId)
    {
        var r = await db.MeterReadings.FirstOrDefaultAsync(x => x.Id == readingId && x.PrinterId == id);
        if (r is not null) { db.MeterReadings.Remove(r); await db.SaveChangesAsync(); }
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var printer = await db.Printers.FindAsync(id);
        if (printer is null) return false;
        Printer = printer;
        Cfg = await db.PrinterMeterConfigs.FindAsync(id);
        Readings = await db.MeterReadings.Where(m => m.PrinterId == id)
            .OrderByDescending(m => m.TakenAt).Take(40).ToListAsync();
        return true;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
