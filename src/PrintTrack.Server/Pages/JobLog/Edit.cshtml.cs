using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.JobLog;

public sealed class EditModel(AppDbContext db, JobLogPollingService poller, IppClient ipp, AdminScope scope) : PageModel
{
    public Printer Printer { get; private set; } = null!;
    public PrinterJobLogConfig? Cfg { get; private set; }
    public bool CanWrite { get; private set; }

    public List<IppClient.IppJob>? IppJobs { get; private set; }
    public string? IppDiag { get; private set; }
    public string? IppError { get; private set; }

    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        public int PrinterId { get; set; }
        public bool Enabled { get; set; }
        public string? BaseUrl { get; set; }
        [Required] public string ReportPath { get; set; } = "/hp/device/JobLogReport";
        [Required] public string ExportFormatId { get; set; } = "7d725274-035c-4588-9fdf-47743bb71df0";
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!scope.CanSeeSite(Printer.SiteId)) return Forbid();
        Input = Cfg is null
            ? new InputModel { PrinterId = id }
            : new InputModel
            {
                PrinterId = id, Enabled = Cfg.Enabled, BaseUrl = Cfg.BaseUrl,
                ReportPath = Cfg.ReportPath, ExportFormatId = Cfg.ExportFormatId,
                Username = Cfg.Username, Password = Cfg.Password
            };
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!await LoadAsync(Input.PrinterId)) return NotFound();
        if (!scope.CanSeeSite(Printer.SiteId)) return Forbid();
        if (!scope.CanWrite) return Forbid();
        if (!await SaveConfigAsync(requireUrl: false)) return Page();

        TempData["Msg"] = "Configuración guardada.";
        return RedirectToPage(new { id = Input.PrinterId });
    }

    /// <summary>Save the form and immediately pull — the "Traer ahora" button.</summary>
    public async Task<IActionResult> OnPostPollAsync(CancellationToken ct)
    {
        if (!await LoadAsync(Input.PrinterId)) return NotFound();
        if (!scope.CanSeeSite(Printer.SiteId)) return Forbid();
        if (!scope.CanWrite) return Forbid();
        if (!await SaveConfigAsync(requireUrl: true)) return Page();

        var (n, err) = await poller.PollOneAsync(Input.PrinterId, User.Identity?.Name ?? "admin", ct);
        // "Job Log no disponible ... importado por IPP: N" is the fallback working as designed —
        // only red-flag it when neither the Job Log nor IPP produced anything.
        if (err is null || err.Contains("importado por IPP:"))
            TempData["Msg"] = $"Configuración guardada. Traído: {n} trabajo(s) nuevo(s). {err}".Trim();
        else TempData["Err"] = $"Config guardada. {err}";
        return RedirectToPage(new { id = Input.PrinterId });
    }

    /// <summary>Diagnostic-only: ask the printer's IPP endpoint (port 631) for completed jobs —
    /// checks whether it exposes job-impressions-completed / job-media-sheets-completed (real page
    /// counts) without needing EWS admin login. Doesn't save/import anything.</summary>
    public async Task<IActionResult> OnPostTestIppAsync(CancellationToken ct)
    {
        if (!await LoadAsync(Input.PrinterId)) return NotFound();
        if (!scope.CanSeeSite(Printer.SiteId)) return Forbid();

        var baseUrl = string.IsNullOrWhiteSpace(Input.BaseUrl) ? Cfg?.BaseUrl : Input.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var u))
        {
            IppError = "Cargá primero la URL base del equipo arriba.";
            return Page();
        }

        var (jobs, err, diag) = await ipp.GetCompletedJobsAsync(u.Host, 631, limit: 200, timeoutSec: 15, ct);
        IppJobs = jobs;
        IppError = err;
        IppDiag = diag;
        return Page();
    }

    private async Task<bool> SaveConfigAsync(bool requireUrl)
    {
        if ((requireUrl || Input.Enabled) && string.IsNullOrWhiteSpace(Input.BaseUrl))
            ModelState.AddModelError("Input.BaseUrl", "Cargá la URL base del equipo (ej. https://192.168.30.4).");
        if (!ModelState.IsValid) return false;

        var cfg = Cfg ?? new PrinterJobLogConfig { PrinterId = Input.PrinterId };
        if (Cfg is null) { db.PrinterJobLogConfigs.Add(cfg); Cfg = cfg; }

        cfg.Enabled = Input.Enabled;
        cfg.BaseUrl = Input.BaseUrl?.Trim().TrimEnd('/');
        cfg.ReportPath = string.IsNullOrWhiteSpace(Input.ReportPath) ? "/hp/device/JobLogReport" : Input.ReportPath.Trim();
        cfg.ExportFormatId = string.IsNullOrWhiteSpace(Input.ExportFormatId)
            ? "7d725274-035c-4588-9fdf-47743bb71df0" : Input.ExportFormatId.Trim();
        cfg.Username = Trim(Input.Username);
        cfg.Password = Input.Password;
        await db.SaveChangesAsync();
        return true;
    }

    private async Task<bool> LoadAsync(int id)
    {
        await scope.LoadAsync();
        var printer = await db.Printers.FindAsync(id);
        if (printer is null) return false;
        Printer = printer;
        CanWrite = scope.CanWrite;
        Cfg = await db.PrinterJobLogConfigs.FindAsync(id);
        return true;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
