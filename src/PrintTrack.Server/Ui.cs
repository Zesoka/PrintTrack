using PrintTrack.Shared;

namespace PrintTrack.Server;

/// <summary>Small presentation helpers shared by the Razor pages.</summary>
public static class Ui
{
    public static string StatusBadge(PrintJobStatus s) => s switch
    {
        PrintJobStatus.Printed => "success",
        PrintJobStatus.Pending => "secondary",
        PrintJobStatus.Cancelled => "secondary",
        PrintJobStatus.Denied => "danger",
        PrintJobStatus.Failed => "dark",
        _ => "secondary"
    };

    public static string StatusText(PrintJobStatus s) => s switch
    {
        PrintJobStatus.Printed => "Impreso",
        PrintJobStatus.Pending => "Pendiente",
        PrintJobStatus.Cancelled => "Cancelado",
        PrintJobStatus.Denied => "Denegado",
        PrintJobStatus.Failed => "Falló",
        _ => s.ToString()
    };

    public static string ColorText(ColorMode c) => c switch
    {
        ColorMode.Color => "Color",
        ColorMode.Grayscale => "B/N",
        _ => "—"
    };

    public static string DuplexText(DuplexMode d) => d switch
    {
        DuplexMode.Duplex => "Dúplex",
        DuplexMode.Simplex => "Simple",
        _ => "—"
    };

    public static string Ago(DateTimeOffset when)
    {
        var d = DateTimeOffset.UtcNow - when;
        if (d < TimeSpan.FromMinutes(1)) return "hace segundos";
        if (d < TimeSpan.FromHours(1)) return $"hace {(int)d.TotalMinutes} min";
        if (d < TimeSpan.FromDays(1)) return $"hace {(int)d.TotalHours} h";
        return $"hace {(int)d.TotalDays} d";
    }
}
