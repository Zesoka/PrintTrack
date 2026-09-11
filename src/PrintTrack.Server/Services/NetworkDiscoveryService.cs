using System.Collections.Concurrent;
using System.Net;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Services;

/// <summary>
/// Server-side network sweep (NOT an agent — nothing installed anywhere) that asks every host in a
/// CIDR range for its SNMP identity (sysName/sysDescr) plus its page counter, the same way
/// <see cref="SnmpMeterReader"/> already does per-printer. Meant to replace typing 300 IPs by hand:
/// point it at a Sede's subnet, get back every HP printer that answered.
/// </summary>
public sealed class NetworkDiscoveryService(SnmpMeterReader snmp, ILogger<NetworkDiscoveryService> logger)
{
    public sealed record DiscoveredPrinter(string Ip, string? Hostname, string? Descr, long? Pages);

    /// <summary>Scans up to <paramref name="maxHosts"/> addresses in <paramref name="cidr"/>
    /// (e.g. "192.168.30.0/24") for anything that answers SNMP with a printer-shaped identity.</summary>
    public async Task<List<DiscoveredPrinter>> ScanAsync(
        string cidr, string community, int timeoutMs, int maxHosts, CancellationToken ct)
    {
        var ips = ExpandCidr(cidr, maxHosts);
        var results = new ConcurrentBag<DiscoveredPrinter>();
        using var gate = new SemaphoreSlim(48);

        var tasks = ips.Select(async ip =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var cfg = new PrinterMeterConfig
                {
                    Host = ip, Port = 161, Version = SnmpVersion.V2c, Community = community,
                    OidTotal = "1.3.6.1.2.1.43.10.2.1.4.1.1"
                };
                var sample = await snmp.ReadAsync(cfg, timeoutMs, ct);
                if (sample.DeviceName is not null || sample.DeviceDescr is not null || sample.Total is not null)
                    results.Add(new DiscoveredPrinter(ip, sample.DeviceName, sample.DeviceDescr, sample.Total));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogDebug(ex, "Descubrimiento: {Ip} no respondió.", ip); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);

        return results.OrderBy(r => IpSortKey(r.Ip)).ToList();
    }

    private static uint IpSortKey(string ip)
    {
        var b = IPAddress.Parse(ip).GetAddressBytes();
        return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
    }

    /// <summary>Expands "a.b.c.d/nn" into individual host IPs (network/broadcast excluded), capped
    /// at <paramref name="maxHosts"/> so a typo like /8 can't trigger a scan of millions of hosts.</summary>
    private static List<string> ExpandCidr(string cidr, int maxHosts)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var baseIp)
            || baseIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out var prefix) || prefix is < 16 or > 32)
            throw new ArgumentException("Formato esperado: 192.168.30.0/24 (prefijo entre /16 y /32).");

        var b = baseIp.GetAddressBytes();
        var baseInt = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        var hostBits = 32 - prefix;
        var count = hostBits == 0 ? 1u : (1u << hostBits);
        var network = hostBits == 32 ? 0u : baseInt & ~(count - 1);

        var skipEdges = prefix < 31;   // /31 and /32 have no separate network/broadcast address
        var start = skipEdges ? 1u : 0u;
        var end = skipEdges ? count - 1 : count;

        var list = new List<string>();
        for (var i = start; i < end && list.Count < maxHosts; i++)
        {
            var host = network + i;
            list.Add(new IPAddress([(byte)(host >> 24), (byte)(host >> 16), (byte)(host >> 8), (byte)host]).ToString());
        }
        return list;
    }
}
