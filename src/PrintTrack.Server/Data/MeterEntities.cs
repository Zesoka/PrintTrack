namespace PrintTrack.Server.Data;

public enum SnmpVersion { V1 = 0, V2c = 1, V3 = 2 }

public enum MeterSource { Snmp = 0, Manual = 1 }

/// <summary>Per-printer SNMP configuration for reading the device's own page counters. 1:1 with <see cref="Printer"/>.</summary>
public sealed class PrinterMeterConfig
{
    public int PrinterId { get; set; }
    public Printer Printer { get; set; } = null!;

    public bool Enabled { get; set; }

    /// <summary>IP or DNS name of the device.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 161;

    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;
    public string Community { get; set; } = "public";

    // SNMP v3 (best-effort)
    public string? SecurityName { get; set; }
    public string? AuthProtocol { get; set; }   // "MD5" | "SHA" | null
    public string? AuthPassword { get; set; }
    public string? PrivProtocol { get; set; }   // "DES" | "AES" | null
    public string? PrivPassword { get; set; }

    /// <summary>Lifetime total impressions. Default = Printer-MIB prtMarkerLifeCount.1.1.</summary>
    public string OidTotal { get; set; } = "1.3.6.1.2.1.43.10.2.1.4.1.1";

    /// <summary>Optional vendor-specific OIDs for the mono / color breakdown.</summary>
    public string? OidMono { get; set; }
    public string? OidColor { get; set; }

    /// <summary>Auto-filled from SNMP <c>sysName</c> (the device's own hostname) on each poll.</summary>
    public string? DeviceName { get; set; }

    /// <summary>Auto-filled from SNMP <c>sysDescr</c> (model / firmware) on each poll.</summary>
    public string? DeviceDescr { get; set; }

    public DateTimeOffset? LastPolledAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>One snapshot of a printer's lifetime counters (absolute values, not deltas).</summary>
public sealed class MeterReading
{
    public long Id { get; set; }

    public int PrinterId { get; set; }
    public Printer Printer { get; set; } = null!;

    public DateTimeOffset TakenAt { get; set; } = DateTimeOffset.UtcNow;
    public MeterSource Source { get; set; } = MeterSource.Snmp;

    public long Total { get; set; }
    public long? Mono { get; set; }
    public long? Color { get; set; }

    public string? Note { get; set; }
    public string CreatedBy { get; set; } = "system";
}
