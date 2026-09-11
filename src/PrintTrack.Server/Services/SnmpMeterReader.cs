using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Services;

public sealed record MeterSample(
    long? Total, long? Mono, long? Color,
    string? DeviceName, string? DeviceDescr, string? Error)
{
    public bool Ok => Error is null && Total is not null;
}

/// <summary>
/// Reads a printer's lifetime page counters + identity over SNMP (v1 / v2c / v3).
/// Default counter OID is Printer-MIB <c>prtMarkerLifeCount.1.1</c>; always also fetches
/// <c>sysName</c> (device hostname) and <c>sysDescr</c> (model / firmware).
/// </summary>
public sealed class SnmpMeterReader(ILogger<SnmpMeterReader> logger)
{
    private const string OidSysDescr = "1.3.6.1.2.1.1.1.0";
    private const string OidSysName = "1.3.6.1.2.1.1.5.0";

    public async Task<MeterSample> ReadAsync(PrinterMeterConfig cfg, int timeoutMs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.Host))
            return new MeterSample(null, null, null, null, null, "Sin host SNMP configurado.");

        IPEndPoint endpoint;
        try
        {
            var ip = await ResolveAsync(cfg.Host!, ct);
            endpoint = new IPEndPoint(ip, cfg.Port <= 0 ? 161 : cfg.Port);
        }
        catch (Exception ex)
        {
            return new MeterSample(null, null, null, null, null, $"No se resolvió '{cfg.Host}': {ex.Message}");
        }

        var oids = new List<(string Oid, string Slot)>
        {
            (cfg.OidTotal, "total"),
            (OidSysName, "name"),
            (OidSysDescr, "descr"),
        };
        if (!string.IsNullOrWhiteSpace(cfg.OidMono)) oids.Add((cfg.OidMono!, "mono"));
        if (!string.IsNullOrWhiteSpace(cfg.OidColor)) oids.Add((cfg.OidColor!, "color"));

        List<Variable> vars;
        try { vars = oids.Select(o => new Variable(new ObjectIdentifier(o.Oid.Trim()))).ToList(); }
        catch (Exception ex) { return new MeterSample(null, null, null, null, null, $"OID inválido: {ex.Message}"); }

        try
        {
            IList<Variable> reply = cfg.Version switch
            {
                SnmpVersion.V1 => await Task.Run(() =>
                    Messenger.Get(VersionCode.V1, endpoint, new OctetString(cfg.Community), vars, timeoutMs), ct),
                SnmpVersion.V2c => await Task.Run(() =>
                    Messenger.Get(VersionCode.V2, endpoint, new OctetString(cfg.Community), vars, timeoutMs), ct),
                SnmpVersion.V3 => await Task.Run(() => GetV3(cfg, endpoint, vars, timeoutMs), ct),
                _ => throw new NotSupportedException()
            };

            long? total = null, mono = null, color = null;
            string? name = null, descr = null;
            foreach (var v in reply)
            {
                var slot = MatchSlot(oids, v.Id.ToString());
                switch (slot)
                {
                    case "total": total = ToLong(v.Data); break;
                    case "mono": mono = ToLong(v.Data); break;
                    case "color": color = ToLong(v.Data); break;
                    case "name": name = ToText(v.Data); break;
                    case "descr": descr = ToText(v.Data); break;
                }
            }

            if (total is null && mono is not null && color is not null) total = mono + color;

            return total is null
                ? new MeterSample(null, mono, color, name, descr,
                    "El OID de total no devolvió un número (¿OID incorrecto o SNMP mal configurado?).")
                : new MeterSample(total, mono, color, name, descr, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SNMP GET falló para {Host}.", cfg.Host);
            return new MeterSample(null, null, null, null, null, ex.Message);
        }
    }

    /// <summary>GET a single arbitrary OID — used by the "probar OID" helper when hunting for
    /// a model's mono/color counter OID.</summary>
    public async Task<(string? Value, string? Error)> GetRawAsync(PrinterMeterConfig cfg, string oid, int timeoutMs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.Host)) return (null, "Sin host SNMP.");
        IPEndPoint endpoint;
        try
        {
            endpoint = new IPEndPoint(await ResolveAsync(cfg.Host!, ct), cfg.Port <= 0 ? 161 : cfg.Port);
        }
        catch (Exception ex) { return (null, ex.Message); }

        List<Variable> vars;
        try { vars = [new Variable(new ObjectIdentifier(oid.Trim()))]; }
        catch (Exception ex) { return (null, $"OID inválido: {ex.Message}"); }

        try
        {
            IList<Variable> reply = cfg.Version switch
            {
                SnmpVersion.V1 => await Task.Run(() => Messenger.Get(VersionCode.V1, endpoint, new OctetString(cfg.Community), vars, timeoutMs), ct),
                SnmpVersion.V2c => await Task.Run(() => Messenger.Get(VersionCode.V2, endpoint, new OctetString(cfg.Community), vars, timeoutMs), ct),
                SnmpVersion.V3 => await Task.Run(() => GetV3(cfg, endpoint, vars, timeoutMs), ct),
                _ => throw new NotSupportedException()
            };
            return (reply.FirstOrDefault()?.Data.ToString() ?? "(sin datos)", null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (null, ex.Message); }
    }

    private static string? MatchSlot(List<(string Oid, string Slot)> oids, string replyOid)
    {
        var norm = replyOid.TrimStart('.');
        foreach (var (oid, slot) in oids)
            if (oid.Trim().TrimStart('.') == norm) return slot;
        return null;
    }

    private static IList<Variable> GetV3(PrinterMeterConfig cfg, IPEndPoint endpoint, IList<Variable> vars, int timeoutMs)
    {
#pragma warning disable CS0618 // MD5/SHA1/DES are legacy but still required by many printers
        IAuthenticationProvider auth = (cfg.AuthProtocol?.ToUpperInvariant()) switch
        {
            "MD5" => new MD5AuthenticationProvider(new OctetString(cfg.AuthPassword ?? "")),
            "SHA" => new SHA1AuthenticationProvider(new OctetString(cfg.AuthPassword ?? "")),
            _ => DefaultAuthenticationProvider.Instance
        };
        IPrivacyProvider priv = (cfg.PrivProtocol?.ToUpperInvariant()) switch
        {
            "DES" => new DESPrivacyProvider(new OctetString(cfg.PrivPassword ?? ""), auth),
            "AES" or "AES128" => new AESPrivacyProvider(new OctetString(cfg.PrivPassword ?? ""), auth),
            _ => new DefaultPrivacyProvider(auth)
        };

        var user = new OctetString(cfg.SecurityName ?? "");
        var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        var report = discovery.GetResponse(timeoutMs, endpoint);

        var request = new GetRequestMessage(
            VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId, user, vars, priv, report);
#pragma warning restore CS0618
        var response = request.GetResponse(timeoutMs, endpoint);
        if (response.Pdu().ErrorStatus.ToInt32() != 0)
            throw new InvalidOperationException($"SNMPv3 error status {response.Pdu().ErrorStatus}.");
        return response.Pdu().Variables;
    }

    private static long? ToLong(ISnmpData data)
    {
        var s = data.ToString();
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;
    }

    private static string? ToText(ISnmpData data)
    {
        var s = data.ToString();
        return string.IsNullOrWhiteSpace(s) || s is "Null" or "NoSuchObject" or "NoSuchInstance" or "EndOfMibView"
            ? null
            : s.Trim();
    }

    private static async Task<IPAddress> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var ip)) return ip;
        var addrs = await Dns.GetHostAddressesAsync(host, ct);
        return addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs.First();
    }
}
