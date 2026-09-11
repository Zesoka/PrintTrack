namespace PrintTrack.Server.Data;

/// <summary>
/// Config for pulling the HP FutureSmart "Registro de trabajos" CSV export from a printer's EWS
/// automatically. 1:1 with <see cref="Printer"/>.
/// </summary>
public sealed class PrinterJobLogConfig
{
    public int PrinterId { get; set; }
    public Printer Printer { get; set; } = null!;

    public bool Enabled { get; set; }

    /// <summary>Device base URL, e.g. <c>https://10.0.12.34</c> (no trailing slash, no path).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>EWS path to the job-log report. Default fits the M630 family.</summary>
    public string ReportPath { get; set; } = "/hp/device/JobLogReport";

    /// <summary>The <c>JobLogRadioButton</c> form value that selects the comma-CSV format.
    /// Default = value observed on the M630; may differ on other models.</summary>
    public string ExportFormatId { get; set; } = "7d725274-035c-4588-9fdf-47743bb71df0";

    /// <summary>Optional EWS admin credentials, for devices that require login to see the job log.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }

    public DateTimeOffset? LastPolledAt { get; set; }
    public string? LastError { get; set; }
    public int LastImported { get; set; }

    /// <summary>First ~8 KB of the last unexpected HTML response, for diagnosing the export flow.</summary>
    public string? LastResponseSnippet { get; set; }
}
