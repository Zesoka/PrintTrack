using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.Discovery;

/// <summary>Server-side network sweep for HP printers (SNMP) — no agent, nothing installed
/// anywhere. Finds candidates in a subnet and bulk-creates Printer + SNMP + Job Log config for
/// the ones you pick, instead of typing every IP by hand.</summary>
[Authorize(Roles = AdminRoles.SuperAdmin)]
public sealed class IndexModel(NetworkDiscoveryService discovery, AppDbContext db) : PageModel
{
    [BindProperty] public string Cidr { get; set; } = "";
    [BindProperty] public string Community { get; set; } = "public";
    [BindProperty] public int? DefaultSiteId { get; set; }

    [BindProperty] public List<RowVm> Rows { get; set; } = [];
    public List<Site> Sites { get; private set; } = [];
    public bool Scanned { get; private set; }

    public sealed class RowVm
    {
        public string Ip { get; set; } = "";
        public string? Hostname { get; set; }
        public string? Descr { get; set; }
        public long? Pages { get; set; }
        public bool AlreadyExists { get; set; }
        public string? ExistingPrinterName { get; set; }
        public bool Selected { get; set; }
        public string Name { get; set; } = "";
        public int? SiteId { get; set; }
    }

    public async Task OnGetAsync() => await LoadSitesAsync();

    public async Task<IActionResult> OnPostScanAsync(CancellationToken ct)
    {
        await LoadSitesAsync();
        if (string.IsNullOrWhiteSpace(Cidr))
        {
            ModelState.AddModelError(nameof(Cidr), "Cargá un rango, ej. 192.168.30.0/24.");
            return Page();
        }

        List<NetworkDiscoveryService.DiscoveredPrinter> found;
        try
        {
            found = await discovery.ScanAsync(
                Cidr.Trim(), string.IsNullOrWhiteSpace(Community) ? "public" : Community.Trim(),
                timeoutMs: 400, maxHosts: 1024, ct);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(Cidr), ex.Message);
            return Page();
        }

        var existing = await db.PrinterMeterConfigs.Include(c => c.Printer)
            .Where(c => c.Host != null)
            .ToDictionaryAsync(c => c.Host!, c => c.Printer.Name, StringComparer.OrdinalIgnoreCase, ct);

        Rows = found.Select(f =>
        {
            var exists = existing.TryGetValue(f.Ip, out var existingName);
            return new RowVm
            {
                Ip = f.Ip, Hostname = f.Hostname, Descr = f.Descr, Pages = f.Pages,
                AlreadyExists = exists, ExistingPrinterName = existingName,
                Selected = !exists,
                Name = f.Hostname is { Length: > 0 } h ? h : f.Ip,
                SiteId = DefaultSiteId
            };
        }).ToList();

        Scanned = true;
        if (Rows.Count == 0)
            ModelState.AddModelError(nameof(Cidr), "No respondió nadie por SNMP en ese rango (¿comunidad incorrecta, o SNMP apagado en esos equipos?).");
        return Page();
    }

    /// <summary>Bulk-create Printer + SNMP config + Job Log config for every checked row.</summary>
    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        var added = 0;
        foreach (var r in Rows.Where(r => r.Selected && !string.IsNullOrWhiteSpace(r.Ip)))
        {
            if (await db.PrinterMeterConfigs.AnyAsync(c => c.Host == r.Ip, ct)) continue;   // added meanwhile

            var printer = new Printer
            {
                Name = string.IsNullOrWhiteSpace(r.Name) ? r.Ip : r.Name.Trim(),
                SiteId = r.SiteId,
                IsTracked = true
            };
            db.Printers.Add(printer);
            await db.SaveChangesAsync(ct);   // need printer.Id for the child configs

            db.PrinterMeterConfigs.Add(new PrinterMeterConfig
            {
                PrinterId = printer.Id, Enabled = true, Host = r.Ip, Port = 161,
                Version = SnmpVersion.V2c, Community = string.IsNullOrWhiteSpace(Community) ? "public" : Community.Trim(),
                OidTotal = "1.3.6.1.2.1.43.10.2.1.4.1.1",
                DeviceName = r.Hostname, DeviceDescr = r.Descr
            });
            db.PrinterJobLogConfigs.Add(new PrinterJobLogConfig
            {
                PrinterId = printer.Id, Enabled = true, BaseUrl = $"https://{r.Ip}",
                ReportPath = "/hp/device/JobLogReport",
                ExportFormatId = "7d725274-035c-4588-9fdf-47743bb71df0"
            });
            added++;
        }
        await db.SaveChangesAsync(ct);
        TempData["Msg"] = $"Se agregaron {added} impresora(s) nueva(s), con SNMP y Job Log ya configurados.";
        return RedirectToPage();
    }

    private async Task LoadSitesAsync() => Sites = await db.Sites.OrderBy(s => s.Name).ToListAsync();
}
