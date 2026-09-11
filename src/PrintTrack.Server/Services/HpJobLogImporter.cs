using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Shared;

namespace PrintTrack.Server.Services;

public sealed record ImportResult(int TotalRows, int Imported, int Duplicates, int SkippedType, int Errors, string? FatalError = null)
{
    public bool Ok => FatalError is null;
}

/// <summary>
/// Imports an HP FutureSmart "Registro de trabajos" export (CSV or tab-separated .txt) into
/// <see cref="PrintJobRecord"/> rows. Columns expected: Usuario, Nombre trab., Tipo, Estado, Fecha/Hora.
/// The export carries no page count, so <c>Sheets</c> stays 0 for imported rows.
/// </summary>
public sealed partial class HpJobLogImporter(AppDbContext db, ILogger<HpJobLogImporter> logger)
{
    private static readonly Dictionary<string, string> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ene"] = "01", ["jan"] = "01", ["feb"] = "02", ["mar"] = "03", ["abr"] = "04", ["apr"] = "04",
        ["may"] = "05", ["jun"] = "06", ["jul"] = "07", ["ago"] = "08", ["aug"] = "08",
        ["sep"] = "09", ["sept"] = "09", ["oct"] = "10", ["nov"] = "11", ["dic"] = "12", ["dec"] = "12",
    };

    public async Task<ImportResult> ImportAsync(
        int printerId, string content, bool onlyPrintJobs, string triggeredBy, CancellationToken ct)
    {
        var printer = await db.Printers.FirstOrDefaultAsync(p => p.Id == printerId, ct);
        if (printer is null) return new ImportResult(0, 0, 0, 0, 0, "Impresora no encontrada.");

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count < 2) return new ImportResult(0, 0, 0, 0, 0, "El archivo no tiene datos.");

        var delimiter = lines[0].Contains('\t') ? '\t' : ',';
        var header = SplitLine(lines[0], delimiter);
        int iUser = FindCol(header, "usuario", "user name", "user");
        int iDoc = FindCol(header, "nombre trab", "job name", "name");
        int iType = FindCol(header, "tipo", "job type", "type");
        int iStatus = FindCol(header, "estado", "result", "status");
        int iDate = FindCol(header, "fecha", "time", "date");
        if (iUser < 0 || iDoc < 0 || iDate < 0)
            return new ImportResult(0, 0, 0, 0, 0, "No se reconocen las columnas (falta Usuario / Nombre trab. / Fecha).");

        var rows = new List<(string User, string Doc, string Type, string Status, DateTimeOffset When, string Ext)>();
        int errors = 0, skippedType = 0;

        for (var li = 1; li < lines.Count; li++)
        {
            var f = SplitLine(lines[li], delimiter);
            if (f.Length <= Math.Max(iUser, Math.Max(iDoc, iDate))) { errors++; continue; }

            var type = Get(f, iType);
            if (onlyPrintJobs && !IsPrintType(type)) { skippedType++; continue; }

            if (!TryParseWhen(Get(f, iDate), out var when)) { errors++; continue; }

            var user = Get(f, iUser).Trim();
            var doc = Get(f, iDoc).Trim();
            var ext = ExternalId(printerId, when, user, doc);
            rows.Add((user, doc, type, Get(f, iStatus), when, ext));
        }

        if (rows.Count == 0)
            return new ImportResult(lines.Count - 1, 0, 0, skippedType, errors);

        // dedup against what's already imported for this printer
        var exts = rows.Select(r => r.Ext).ToHashSet();
        var existing = await db.PrintJobs
            .Where(j => j.PrinterId == printerId && j.ExternalId != null && exts.Contains(j.ExternalId))
            .Select(j => j.ExternalId!).ToListAsync(ct);
        var known = existing.ToHashSet();

        var fresh = rows.Where(r => known.Add(r.Ext)).ToList();  // Add() false => already seen (file dup too)
        var duplicates = rows.Count - fresh.Count;
        if (fresh.Count == 0)
            return new ImportResult(lines.Count - 1, 0, duplicates, skippedType, errors);

        // resolve / create users
        var byNorm = new Dictionary<string, int>();
        var distinctUsers = fresh.Select(r => r.User).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var norms = distinctUsers.ToDictionary(u => u, Naming.NormalizeUser);
        var normSet = norms.Values.Where(n => n.Length > 0).ToHashSet();
        foreach (var eu in await db.EndUsers.Where(u => normSet.Contains(u.NormalizedUserName)).ToListAsync(ct))
            byNorm[eu.NormalizedUserName] = eu.Id;

        foreach (var raw in distinctUsers)
        {
            var norm = norms[raw];
            if (norm.Length == 0) norm = "INVITADO";
            if (byNorm.ContainsKey(norm)) continue;
            var created = new EndUser
            {
                UserName = string.IsNullOrWhiteSpace(raw) ? "Invitado" : raw,
                NormalizedUserName = norm,
                AutoCreated = true,
                LastSeenAt = DateTimeOffset.UtcNow
            };
            db.EndUsers.Add(created);
            await db.SaveChangesAsync(ct);
            byNorm[norm] = created.Id;
        }

        foreach (var r in fresh)
        {
            var norm = Naming.NormalizeUser(r.User);
            if (norm.Length == 0) norm = "INVITADO";
            db.PrintJobs.Add(new PrintJobRecord
            {
                JobRef = "hpimp:" + r.Ext,
                EndUserId = byNorm[norm],
                UserNameRaw = string.IsNullOrWhiteSpace(r.User) ? "Invitado" : r.User,
                PrinterId = printerId,
                PrinterNameRaw = printer.Name,
                SiteId = printer.SiteId,
                DocumentName = Truncate(string.IsNullOrWhiteSpace(r.Doc) ? "(sin nombre)" : r.Doc, 512),
                Pages = 0,
                Copies = 1,
                Sheets = 0,
                Color = ColorMode.Unknown,
                Duplex = DuplexMode.Unknown,
                Status = MapStatus(r.Status),
                Source = JobSource.HpJobLog,
                ExternalId = r.Ext,
                SubmittedAt = r.When,
                DecidedAt = r.When,
                CompletedAt = r.When,
                DecisionMessage = string.IsNullOrWhiteSpace(r.Type) ? null : $"Tipo: {r.Type}"
            });
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Import HP Job Log printer={Printer} by={By}: {Imported} nuevos, {Dup} duplicados, {SkipT} no-impresión, {Err} errores.",
            printer.Name, triggeredBy, fresh.Count, duplicates, skippedType, errors);

        return new ImportResult(lines.Count - 1, fresh.Count, duplicates, skippedType, errors);
    }

    private static bool IsPrintType(string type)
    {
        var t = type.Trim().ToLowerInvariant();
        return t is "imprimir" or "print" || t.StartsWith("imprim") || t.StartsWith("print");
    }

    private static PrintJobStatus MapStatus(string status) => status.Trim().ToLowerInvariant() switch
    {
        "correcto" or "completed" or "ok" or "success" or "" => PrintJobStatus.Printed,
        "cancelado" or "canceled" or "cancelled" => PrintJobStatus.Cancelled,
        _ when status.Contains("error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("fall", StringComparison.OrdinalIgnoreCase) => PrintJobStatus.Failed,
        _ => PrintJobStatus.Printed
    };

    private static bool TryParseWhen(string raw, out DateTimeOffset when)
    {
        when = default;
        var s = raw.Trim();
        // normalize "10/Sep/2026 11:04:32" -> "10/09/2026 11:04:32"
        var m = MonthTokenRegex().Match(s);
        if (m.Success && Months.TryGetValue(m.Groups[1].Value, out var mm))
            s = s[..m.Groups[1].Index] + mm + s[(m.Groups[1].Index + m.Groups[1].Length)..];

        string[] formats =
        {
            "dd/MM/yyyy HH:mm:ss", "d/M/yyyy HH:mm:ss", "dd/MM/yyyy H:mm:ss",
            "yyyy-MM-dd HH:mm:ss", "MM/dd/yyyy hh:mm:ss tt", "M/d/yyyy h:mm:ss tt",
            "dd/MM/yyyy HH:mm", "yyyy/MM/dd HH:mm:ss"
        };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var dt))
        {
            when = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
            return true;
        }
        return false;
    }

    private static string ExternalId(int printerId, DateTimeOffset when, string user, string doc)
    {
        var key = $"{printerId}|{when.ToUnixTimeSeconds()}|{Naming.NormalizeUser(user)}|{doc.Trim().ToLowerInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..40].ToLowerInvariant();
    }

    private static int FindCol(string[] header, params string[] names)
    {
        for (var i = 0; i < header.Length; i++)
        {
            var h = header[i].Trim().ToLowerInvariant();
            if (names.Any(n => h.Contains(n))) return i;
        }
        return -1;
    }

    private static string Get(string[] f, int i) => i >= 0 && i < f.Length ? f[i] : "";

    /// <summary>Split one line: tab = plain split; comma = minimal CSV (handles "quoted, fields").</summary>
    private static string[] SplitLine(string line, char delimiter)
    {
        if (delimiter == '\t') return line.Split('\t');

        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    [GeneratedRegex(@"[/\- ]([A-Za-z]{3,4})[/\- ]")]
    private static partial Regex MonthTokenRegex();
}
