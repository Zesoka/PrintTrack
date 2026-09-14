using System.Globalization;
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

    /// <summary>Fixed dd/MM/yyyy HH:mm rendering for every absolute date shown in the app — never
    /// ".ToString(\"g\")", which silently follows whatever culture the container/OS happens to have
    /// (and made the same field print differently depending on the box it ran on).</summary>
    public static string Dt(DateTimeOffset when) => when.LocalDateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Translate a raw LastError string (Job Log or SNMP) into a short label + plain-language
    /// explanation/next-step, from the patterns seen across the real fleet.</summary>
    public static (string Label, string Explanation) ExplainError(string err)
    {
        var e = err.ToLowerInvariant();

        if (e.Contains("pide login") || e.Contains("access denied") || e.Contains("sign in") || e.Contains("401"))
            return ("Pide login", "El equipo exige credenciales para ver el registro. Cargá usuario/clave del EWS en «Credenciales» dentro de su config de Job Log.");

        if (e.Contains("importado por ipp:"))
            return ("Job Log bloqueado, cubierto por IPP", "Es informativo, no un error real: el Job Log no está disponible pero IPP trajo los datos igual. Revisá que el número de trabajos no sea 0.");

        if (e.Contains("http 404"))
            return ("Ruta no encontrada (404)", "El Job Log no existe en esa ruta — típico de modelos viejos sin FutureSmart. Probá «Probar IPP», y si tampoco da nada, usá Contadores (SNMP) para el total de páginas.");

        if (e.Contains("connection refused") || e.Contains("no connection could be made"))
            return ("Conexión rechazada", "El equipo no responde en ese puerto — puede estar apagado, en otra IP, en modo reposo profundo, o no tener ese servicio habilitado.");

        if (e.Contains("no se pudo resolver") || e.Contains("no such host") || e.Contains("name or service not known"))
            return ("No resuelve el host", "La IP/host cargado no responde a nivel de red. Revisá que esté bien escrita y que el equipo esté encendido y en esa dirección.");

        if (e.Contains("ssl") || e.Contains("tls") || e.Contains("handshake") || e.Contains("secure channel") || e.Contains("authentication failed"))
            return ("Problema de TLS/SSL", "El equipo no completa el handshake seguro — puede ser un modelo con cifrado muy viejo. El sistema ya reintenta por HTTP simple; si sigue fallando, es limitación del equipo.");

        if (e.Contains("no dio una respuesta ipp usable") || e.Contains("no dio respuesta ipp"))
            return ("Sin IPP", "El equipo no contesta el protocolo IPP en el puerto 631 — modelo sin esa función, o el puerto está cerrado.");

        if (e.Contains("no devolvió trabajos completados"))
            return ("IPP sin historial", "El protocolo respondió pero no hay trabajos completados registrados — puede que ese modelo no retenga historial, o que no haya actividad reciente.");

        if (e.Contains("sin host") || e.Contains("sin url"))
            return ("Falta configurar", "Todavía no se cargó el host/IP en la config de esta impresora.");

        if (e.Contains("oid"))
            return ("OID inválido o sin dato", "El OID de contador no devolvió un número — este modelo puede usar un OID distinto al estándar. Probá «Probar OID» con otro valor.");

        if (e.Contains("hueco entre lecturas"))
            return ("Posible hueco de datos", "El buffer del equipo puede haber rotado más rápido que el sondeo — bajá el intervalo de sondeo si esta impresora es muy activa.");

        return ("Otro", "Revisá el Diagnóstico en la config de esta impresora para el detalle completo.");
    }
}
