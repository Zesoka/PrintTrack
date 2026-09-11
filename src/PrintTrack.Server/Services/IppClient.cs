using System.Net.Security;
using System.Text;

namespace PrintTrack.Server.Services;

/// <summary>
/// Minimal hand-rolled IPP/1.1 client (RFC 8010 wire format) — just enough to ask a printer for its
/// completed jobs (<c>Get-Jobs</c>, <c>which-jobs=completed</c>) and read
/// <c>job-impressions-completed</c> / <c>job-media-sheets-completed</c>: the real per-job page count
/// that HP's Job Log HTML never exposes, over a standard protocol instead of scraping EWS pages.
/// No IPP library dependency; the format is a short TLV encoding, doable directly over HTTP POST.
/// </summary>
public sealed class IppClient(ILogger<IppClient> logger)
{
    private static readonly string[] CommonPaths = ["/ipp/print", "/ipp", "/"];

    public sealed class IppJob
    {
        public Dictionary<string, List<object>> Attrs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Get(string name) => Attrs.TryGetValue(name, out var l) && l.Count > 0 ? l[0]?.ToString() : null;
        public int? GetInt(string name) => Attrs.TryGetValue(name, out var l) && l.Count > 0 && l[0] is int i ? i : null;
        public DateTimeOffset? GetDateTimeOffset(string name) =>
            Attrs.TryGetValue(name, out var l) && l.Count > 0 && l[0] is DateTimeOffset d ? d : null;
    }

    /// <summary>Tries each common IPP resource path until one answers with a parseable IPP response.</summary>
    public async Task<(List<IppJob>? Jobs, string? Error, string Diag)> GetCompletedJobsAsync(
        string host, int port, int limit, int timeoutSec, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host))
            return (null, "Sin host de impresora (URL base vacía o inválida).", "");

        using var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true }
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSec)) };
        var diag = new StringBuilder();

        foreach (var path in CommonPaths)
        {
            try
            {
                var httpUri = new Uri($"http://{host}:{port}{path}");
                var printerUri = $"ipp://{host}{(port == 631 ? "" : ":" + port)}{path}";
                var reqBytes = BuildGetJobsRequest(printerUri, limit);
                using var content = new ByteArrayContent(reqBytes);
                content.Headers.Add("Content-Type", "application/ipp");
                using var resp = await http.PostAsync(httpUri, content, ct);
                var respBytes = await resp.Content.ReadAsByteArrayAsync(ct);
                diag.Append("POST ").Append(httpUri).Append(" -> HTTP ").Append((int)resp.StatusCode)
                    .Append(", ").Append(respBytes.Length).Append(" bytes\n");

                if (!resp.IsSuccessStatusCode || respBytes.Length < 8)
                {
                    diag.Append("  (no llegó una respuesta IPP usable)\n");
                    continue;
                }

                var (status, jobs, parseErr) = ParseGetJobsResponse(respBytes);
                diag.Append("  IPP status: 0x").Append(status.ToString("X4"));
                if (parseErr is not null) diag.Append(" — error de parseo: ").Append(parseErr);
                if (jobs is not null) diag.Append(" — ").Append(jobs.Count).Append(" trabajo(s)");
                diag.Append('\n');

                if (parseErr is null && jobs is not null && (status is 0x0000 or 0x0001 or 0x0002 || jobs.Count > 0))
                    return (jobs, null, diag.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diag.Append("POST http://").Append(host).Append(':').Append(port).Append(path)
                    .Append(" -> ").Append(ex.Message).Append('\n');
            }
        }
        logger.LogDebug("IPP Get-Jobs sin respuesta usable contra {Host}:{Port}.", host, port);
        return (null, "El equipo no dio una respuesta IPP usable en ninguna ruta probada (/ipp/print, /ipp, /).", diag.ToString());
    }

    // ---- request encoding (RFC 8010 §3) --------------------------------------------------------

    private static byte[] BuildGetJobsRequest(string printerUri, int limit)
    {
        using var ms = new MemoryStream();

        void WByte(int b) => ms.WriteByte((byte)b);
        void WShort(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void WInt32(int v) { ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16)); ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void WAttr(int valueTag, string? name, byte[] value)
        {
            WByte(valueTag);
            if (name is null) WShort(0);
            else { var nb = Encoding.UTF8.GetBytes(name); WShort(nb.Length); ms.Write(nb, 0, nb.Length); }
            WShort(value.Length);
            ms.Write(value, 0, value.Length);
        }
        void WStr(int valueTag, string? name, string value) => WAttr(valueTag, name, Encoding.UTF8.GetBytes(value));
        void WInt(int valueTag, string name, int value)
        {
            var b = new byte[4];
            b[0] = (byte)(value >> 24); b[1] = (byte)(value >> 16); b[2] = (byte)(value >> 8); b[3] = (byte)value;
            WAttr(valueTag, name, b);
        }

        WByte(0x01); WByte(0x01);           // IPP/1.1
        WShort(0x000A);                     // operation-id: Get-Jobs
        WInt32(1);                          // request-id
        WByte(0x01);                        // operation-attributes-tag
        WStr(0x47, "attributes-charset", "utf-8");
        WStr(0x48, "attributes-natural-language", "en-us");
        WStr(0x45, "printer-uri", printerUri);
        WStr(0x42, "requesting-user-name", "auditor-impresiones");
        WStr(0x44, "which-jobs", "completed");
        WInt(0x21, "limit", limit);
        // requested-attributes: multi-valued keyword — first value carries the name, the rest don't.
        WStr(0x44, "requested-attributes", "job-id");
        foreach (var n in new[]
                 {
                     "job-name", "job-originating-user-name", "job-impressions-completed",
                     "job-media-sheets-completed", "job-state", "time-at-completed", "date-time-at-completed"
                 })
            WAttr(0x44, null, Encoding.UTF8.GetBytes(n));
        WByte(0x03);                        // end-of-attributes-tag
        return ms.ToArray();
    }

    // ---- response decoding (RFC 8010 §3) -------------------------------------------------------

    private static (int Status, List<IppJob>? Jobs, string? Error) ParseGetJobsResponse(byte[] data)
    {
        try
        {
            var i = 0;
            int RByte() => data[i++];
            int RShort() { var v = (data[i] << 8) | data[i + 1]; i += 2; return v; }
            int RInt32() { var v = (data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]; i += 4; return v; }

            RByte(); RByte();               // version major/minor (unused)
            var status = RShort();
            RInt32();                       // request-id (unused)

            var jobs = new List<IppJob>();
            IppJob? current = null;
            string? lastName = null;

            while (i < data.Length)
            {
                var tag = RByte();
                if (tag == 0x03) break;     // end-of-attributes-tag
                if (tag <= 0x0f)
                {
                    current = tag == 0x02 ? new IppJob() : null;   // 0x02 = job-attributes-tag (new job)
                    if (current is not null) jobs.Add(current);
                    lastName = null;
                    continue;
                }

                var nameLen = RShort();
                string? name;
                if (nameLen > 0) { name = Encoding.UTF8.GetString(data, i, nameLen); i += nameLen; lastName = name; }
                else name = lastName;       // name-length 0 => additional value of the previous attribute

                var valLen = RShort();
                var value = DecodeValue(tag, data, i, valLen);
                i += valLen;

                if (current is not null && name is not null)
                {
                    if (!current.Attrs.TryGetValue(name, out var list)) current.Attrs[name] = list = [];
                    list.Add(value);
                }
            }
            return (status, jobs, null);
        }
        catch (Exception ex) { return (0, null, ex.Message); }
    }

    private static object DecodeValue(int tag, byte[] data, int offset, int len)
    {
        if (tag is 0x21 or 0x23)   // integer / enum
            return len == 4
                ? (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]
                : 0;
        if (tag == 0x22) return len == 1 && data[offset] != 0;   // boolean
        if (tag is 0x10 or 0x12 or 0x13) return "";               // unsupported/unknown/no-value
        if (tag == 0x31 && len == 11)   // dateTime (RFC 2579): year(2) mo day hr min sec decisec dir hrOff minOff
        {
            try
            {
                int year = (data[offset] << 8) | data[offset + 1];
                int month = data[offset + 2], day = data[offset + 3], hour = data[offset + 4],
                    minute = data[offset + 5], second = data[offset + 6], deciSec = data[offset + 7];
                var sign = data[offset + 8] == (byte)'-' ? -1 : 1;
                var tzOffset = new TimeSpan(sign * data[offset + 9], sign * data[offset + 10], 0);
                // Devices report this in whatever offset their IPP stack happens to be set to
                // (seen a factory-default -08:00 on one printer, unrelated to its EWS clock) —
                // always normalize to UTC so it's safe to store regardless of the source offset.
                var local = new DateTimeOffset(year, month, day, hour, minute, second, tzOffset).AddMilliseconds(deciSec * 100);
                return local.ToUniversalTime();
            }
            catch { /* fall through to text/hex below */ }
        }
        if (tag is 0x35 or 0x36)   // textWithLanguage / nameWithLanguage: 2B langLen+lang, 2B textLen+text
        {
            try
            {
                var p = offset;
                var langLen = (data[p] << 8) | data[p + 1]; p += 2 + langLen;
                var textLen = (data[p] << 8) | data[p + 1]; p += 2;
                return Encoding.UTF8.GetString(data, p, textLen);
            }
            catch { /* fall through to flat decode below */ }
        }
        // Every other tag (text/name/keyword/uri/octetString/...) — this HP stack sends
        // job-originating-user-name etc. as plain UTF-8 bytes under tags outside the "official"
        // text-syntax list, so just decode anything that isn't integer/boolean as text.
        try { return Encoding.UTF8.GetString(data, offset, len); }
        catch { return Convert.ToHexString(data, offset, len); }
    }
}
