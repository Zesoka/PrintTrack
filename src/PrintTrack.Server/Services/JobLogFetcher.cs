using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Services;

/// <summary>
/// Pulls the HP FutureSmart "Registro de trabajos" straight off the EWS by <b>scraping the Job Log
/// HTML table</b> (<c>GET /hp/device/JobLogReport/Edit</c>) instead of driving the built-in export.
///
/// Why: on the LaserJet M630 (and siblings) the CSV "Export All" is an async operation the device
/// aborts for an unauthenticated guest session ("the operation failed / verify your configuration"),
/// and it needs the EWS admin password we don't have. But the Job Log <i>list</i> itself is visible
/// to guests, and every column we need — user, document, type, status, date — is right there in the
/// table. We parse those rows and hand the existing <see cref="HpJobLogImporter"/> a synthetic CSV
/// in the exact shape it already imports. No agent, no admin, no async export.
///
/// Limitation: the list has no page count, so imported jobs keep <c>Sheets = 0</c> ("s/d"). Pair
/// with the SNMP page counters (/Meters) for per-printer volume.
///
/// Accepts self-signed certs, negotiates legacy TLS, and retries over plain HTTP if TLS fails.
/// </summary>
public sealed class JobLogFetcher(ILogger<JobLogFetcher> logger)
{
    public async Task<(string? Csv, string? Error, string? Snippet)> FetchCsvAsync(
        PrinterJobLogConfig cfg, int timeoutSec, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl))
            return (null, "Sin URL base configurada.", null);
        if (!Uri.TryCreate(cfg.BaseUrl.TrimEnd('/'), UriKind.Absolute, out var baseUri))
            return (null, $"URL base inválida: {cfg.BaseUrl}", null);

        var (csv, err, snip) = await TryScrapeAsync(baseUri, cfg, timeoutSec, ct);
        if (csv is not null) return (csv, null, null);

        if (baseUri.Scheme == Uri.UriSchemeHttps && IsTlsFailure(err))
        {
            var httpUri = new UriBuilder(baseUri) { Scheme = Uri.UriSchemeHttp, Port = baseUri.IsDefaultPort ? 80 : baseUri.Port }.Uri;
            logger.LogInformation("HTTPS falló ({Err}); reintentando por HTTP contra {Host}.", err, httpUri.Host);
            var (csv2, err2, snip2) = await TryScrapeAsync(httpUri, cfg, timeoutSec, ct);
            if (csv2 is not null) return (csv2, null, null);
            return (null, $"HTTPS: {err} · HTTP: {err2}", snip2 ?? snip);
        }
        return (null, err, snip);
    }

    private async Task<(string? Csv, string? Error, string? Snippet)> TryScrapeAsync(
        Uri baseUri, PrinterJobLogConfig cfg, int timeoutSec, CancellationToken ct)
    {
        var authority = baseUri.GetLeftPart(UriPartial.Authority);
        var reportPath = "/" + cfg.ReportPath.Trim().Trim('/');
        var indexUrl = new Uri($"{authority}{reportPath}/Index");
        var editUrl = new Uri($"{authority}{reportPath}/Edit?jsAnchor=JobLogReportViewSectionId");

        var handler = new SocketsHttpHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
#pragma warning disable SYSLIB0039 // old printer web servers only speak TLS 1.0/1.1 — required here
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls
#pragma warning restore SYSLIB0039
            }
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSec)) };
        http.DefaultRequestHeaders.Add("User-Agent", "AuditorImpresiones/1.0");
        http.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,*/*");

        var diag = new StringBuilder();
        try
        {
            await SafeGet(http, indexUrl, ct);

            var resp = await http.GetAsync(editUrl, ct);
            var html = Decode(await resp.Content.ReadAsByteArrayAsync(ct));
            var url = resp.RequestMessage?.RequestUri ?? editUrl;
            Log(diag, "GET", url, (int)resp.StatusCode, html);

            if (LooksLikeSignIn(url.ToString(), html))
            {
                if (string.IsNullOrWhiteSpace(cfg.Username))
                    return (null, "El equipo pide login para ver el registro. Cargá usuario/clave en la config.", diag.ToString());
                await TryLoginAsync(http, authority, cfg, ct);
                resp = await http.GetAsync(editUrl, ct);
                html = Decode(await resp.Content.ReadAsByteArrayAsync(ct));
                url = resp.RequestMessage?.RequestUri ?? editUrl;
                Log(diag, "GET (post-login)", url, (int)resp.StatusCode, html);
            }

            if (!resp.IsSuccessStatusCode)
                return (null, $"El equipo respondió HTTP {(int)resp.StatusCode} al pedir el registro.", diag.ToString());

            var rows = await ParseJobLogTableAsync(html, url);
            if (rows is null)
            {
                // maybe the table lives on /Index instead of /Edit — try once more
                resp = await http.GetAsync(indexUrl, ct);
                html = Decode(await resp.Content.ReadAsByteArrayAsync(ct));
                Log(diag, "GET /Index", indexUrl, (int)resp.StatusCode, html);
                rows = await ParseJobLogTableAsync(html, indexUrl);
            }
            if (rows is null)
                return (null, "No se encontró la tabla del registro de trabajos (`#JobLogTable`) en la página del equipo. " +
                              "Revisá la URL base / ruta, o mirá el Diagnóstico.", diag.ToString());
            if (rows.Count == 0)
                return (null, "El registro de trabajos del equipo está vacío.", diag.ToString());

            await EnrichPrintDetailsAsync(http, authority, rows, diag, ct);

            return (BuildCsv(rows), null, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message is { Length: > 0 } inner ? $"{ex.Message} ({inner})" : ex.Message;
            logger.LogDebug(ex, "Scrape Job Log falló para {Url}.", baseUri);
            return (null, msg, diag.Length > 0 ? diag.ToString() : null);
        }
        finally { handler.Dispose(); }
    }

    private sealed record JobRow(string User, string Doc, string Type, string Status, string Date, string? DeviceId)
    {
        public string? Copies { get; set; }
        public string? Sides { get; set; }
        public string? Color { get; set; }
        public string? PaperSize { get; set; }
    }

    /// <summary>Parse <c>#JobLogTable</c>. Returns null if the table isn't there at all.</summary>
    private static async Task<List<JobRow>?> ParseJobLogTableAsync(string html, Uri pageUrl)
    {
        var ctx = BrowsingContext.New(Configuration.Default);
        using var doc = await ctx.OpenAsync(r => r.Content(html).Address(pageUrl.ToString()));

        var table = doc.QuerySelector("table#JobLogTable") ?? doc.QuerySelector("#jobLogList table");
        if (table is null) return null;

        var rows = new List<JobRow>();
        foreach (var tr in table.QuerySelectorAll("tbody > tr"))
        {
            var tds = tr.QuerySelectorAll("td").ToList();
            if (tds.Count < 5) continue;

            // Column order on the M630: [0] radio · [1] Job/name · [2] User · [3] Status · [4] Date.
            // Prefer the id-prefixed cells when present, fall back to position.
            string Cell(string idPrefix, int pos) =>
                (tr.QuerySelector($"td[id^='{idPrefix}']")?.TextContent
                 ?? (pos < tds.Count ? tds[pos].TextContent : "")).Trim();

            var doc0 = Cell("JobLogName_", 1);
            var user = Cell("JobLogUser_", 2);
            var status = Cell("JobLogStatus_", 3);
            var date = Cell("JobLogDate_", 4);
            var type = TypeFromRowClass(tr.ClassName);
            var deviceId = tr.QuerySelector("input[type='radio']")?.GetAttribute("value");

            if (date.Length == 0 && user.Length == 0 && doc0.Length == 0) continue;
            if (string.Equals(user, "Guest", StringComparison.OrdinalIgnoreCase)) user = "Invitado";
            rows.Add(new JobRow(user, doc0, type, status, date, deviceId));
        }
        return rows;
    }

    /// <summary>
    /// For each print job, fetch <c>JobLogReportDetails/Index?id=&lt;rowGuid&gt;</c> — a read-only
    /// "Propiedad/Valor" table (<c>#JobDetails</c>) that has real Copias/Caras/Color (no page count
    /// anywhere on this device, confirmed). Best-effort: a failed detail fetch just leaves that
    /// row's extra fields blank, it never fails the whole pull.
    /// </summary>
    private static async Task EnrichPrintDetailsAsync(
        HttpClient http, string authority, List<JobRow> rows, StringBuilder diag, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            if (!row.Type.Equals("Print", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(row.DeviceId))
                continue;
            try
            {
                var url = new Uri($"{authority}/hp/device/JobLogReportDetails/Index?id={row.DeviceId}" +
                    "&StepBackController=JobLogReport&StepBackAction=Edit&StepBackAnchor=JobLogReportViewSectionId&jsAnchor=JobLogReportViewSectionId");
                using var resp = await http.GetAsync(url, ct);
                var html = Decode(await resp.Content.ReadAsByteArrayAsync(ct));
                if (!resp.IsSuccessStatusCode) continue;

                var props = await ParseDetailPropsAsync(html, url);
                if (props is null) continue;
                row.Copies = FindProp(props, "copias", "copies");
                row.Sides = FindProp(props, "caras", "sides", "duplex");
                row.Color = FindProp(props, "color");
                row.PaperSize = FindProp(props, "tam", "size", "salida", "output");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                diag.Append("Detalle de ").Append(row.DeviceId).Append(" falló: ").Append(ex.Message).Append('\n');
            }
        }
    }

    /// <summary>The <c>#JobDetails</c> table on JobLogReportDetails is a plain 2-column
    /// label/value table — read every row as-is, tolerant of whatever labels the device uses.</summary>
    private static async Task<Dictionary<string, string>?> ParseDetailPropsAsync(string html, Uri pageUrl)
    {
        var ctx = BrowsingContext.New(Configuration.Default);
        using var doc = await ctx.OpenAsync(r => r.Content(html).Address(pageUrl.ToString()));

        var table = doc.QuerySelector("table#JobDetails");
        if (table is null) return null;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tr in table.QuerySelectorAll("tbody > tr"))
        {
            var cells = tr.QuerySelectorAll("td").ToList();
            if (cells.Count != 2) continue;
            var label = cells[0].TextContent.Trim().TrimEnd(':').Trim();
            var value = cells[1].TextContent.Trim();
            if (label.Length > 0) map[label] = value;
        }
        return map.Count > 0 ? map : null;
    }

    private static string? FindProp(Dictionary<string, string> map, params string[] needles) =>
        map.FirstOrDefault(kv => needles.Any(n => kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase))).Value;

    /// <summary>HP tags each <c>&lt;tr&gt;</c> with a job-type class (e.g. <c>PrintJobTicket</c>).</summary>
    private static string TypeFromRowClass(string? cls)
    {
        var c = (cls ?? "").Trim();
        if (c.Contains("Print", StringComparison.OrdinalIgnoreCase)) return "Print";   // incl. SecurePrint
        if (c.Contains("Copy", StringComparison.OrdinalIgnoreCase)) return "Copy";
        if (c.Contains("Email", StringComparison.OrdinalIgnoreCase)) return "Email";
        if (c.Contains("Fax", StringComparison.OrdinalIgnoreCase)) return "Fax";
        if (c.Contains("Folder", StringComparison.OrdinalIgnoreCase)) return "Folder";
        if (c.Contains("Http", StringComparison.OrdinalIgnoreCase)) return "HTTP";
        if (c.Contains("Usb", StringComparison.OrdinalIgnoreCase)) return "USB";
        var i = c.IndexOf("JobTicket", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? c[..i] : (c.Length > 0 ? c : "Otro");
    }

    /// <summary>Emit the CSV shape <see cref="HpJobLogImporter"/> already parses and dedups, plus the
    /// Copias/Caras/Color/Tamaño columns filled in for print jobs by <see cref="EnrichPrintDetailsAsync"/>.</summary>
    private static string BuildCsv(IEnumerable<JobRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("Usuario,Nombre trab.,Tipo,Estado,Fecha/Hora,Copias,Caras,Color,Tamaño\n");
        foreach (var r in rows)
            sb.Append(Q(r.User)).Append(',').Append(Q(r.Doc)).Append(',').Append(Q(r.Type)).Append(',')
              .Append(Q(r.Status)).Append(',').Append(Q(r.Date)).Append(',')
              .Append(Q(r.Copies ?? "")).Append(',').Append(Q(r.Sides ?? "")).Append(',')
              .Append(Q(r.Color ?? "")).Append(',').Append(Q(r.PaperSize ?? "")).Append('\n');
        return sb.ToString();

        static string Q(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Contains('"') || s.Contains(',') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }
    }

    private static void Log(StringBuilder diag, string what, Uri url, int status, string body)
    {
        diag.Append("=== ").Append(what).Append(' ').Append(url).Append(" (").Append(status).Append(") ===\n")
            .Append(Trunc(body, 16000)).Append("\n\n");
    }

    private static async Task SafeGet(HttpClient http, Uri url, CancellationToken ct)
    {
        try { using var _ = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch { /* best-effort session bootstrap */ }
    }

    // Not just TLS-specific errors: a "connection refused" on 443 usually means the device simply
    // doesn't speak HTTPS at all (HTTP-only EWS) — worth the same HTTP retry as a broken handshake.
    private static bool IsTlsFailure(string? err) =>
        err is not null && (err.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || err.Contains("TLS", StringComparison.OrdinalIgnoreCase)
            || err.Contains("handshake", StringComparison.OrdinalIgnoreCase)
            || err.Contains("secure channel", StringComparison.OrdinalIgnoreCase)
            || err.Contains("refused", StringComparison.OrdinalIgnoreCase)
            || err.Contains("connection could be made", StringComparison.OrdinalIgnoreCase));

    private static async Task TryLoginAsync(HttpClient http, string authority, PrinterJobLogConfig cfg, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = cfg.Username ?? "",
            ["Password"] = cfg.Password ?? "",
            ["SignIn"] = "Sign In",
        });
        try { using var _ = await http.PostAsync($"{authority}/hp/device/SignIn/Index", form, ct); }
        catch { /* the scrape attempt will surface the real error */ }
    }

    private static bool LooksLikeSignIn(string? url, string html) =>
        (url is not null && url.Contains("SignIn", StringComparison.OrdinalIgnoreCase))
        || (html.Contains("name=\"Password\"", StringComparison.OrdinalIgnoreCase)
            && html.Contains("SignIn", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("id=\"JobLogTable\"", StringComparison.OrdinalIgnoreCase));

    private static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch { return Encoding.Latin1.GetString(bytes); }
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
