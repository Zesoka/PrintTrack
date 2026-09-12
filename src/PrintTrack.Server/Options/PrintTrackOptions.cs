namespace PrintTrack.Server.Options;

public sealed class PrintTrackOptions
{
    public const string SectionName = "PrintTrack";

    /// <summary>Create an <c>EndUser</c> row automatically the first time a login prints.</summary>
    public bool AutoCreateUsers { get; set; } = true;

    /// <summary>Seed admin account created on first run if no admin exists.</summary>
    public string SeedAdminUser { get; set; } = "admin";
    public string SeedAdminEmail { get; set; } = "admin@local";
    public string SeedAdminPassword { get; set; } = "ChangeMe!123";

    public MeterOptions Meter { get; set; } = new();
    public JobLogOptions JobLog { get; set; } = new();
}

public sealed class JobLogOptions
{
    /// <summary>Master switch for the automatic HP job-log pull background service.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minutes between automatic pulls. The device job log is a ring buffer —
    /// keep this well under the time it takes a busy printer to overflow it.</summary>
    public int PollMinutes { get; set; } = 20;

    /// <summary>HTTP timeout per device, seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Max printers polled concurrently. Sequential would take ~300 × several seconds at
    /// fleet scale — easily longer than PollMinutes — so this is bounded parallelism, not serial.</summary>
    public int MaxParallelism { get; set; } = 12;

    /// <summary>IANA id of the time zone the printer's own clock (and its Job Log timestamps) is
    /// set to. The device reports plain local time with no offset — we need this to convert it to
    /// real UTC for storage. Argentina has used UTC-3 year-round (no DST) since 2009.</summary>
    public string DeviceTimeZoneId { get; set; } = "America/Argentina/Buenos_Aires";
}

public sealed class MeterOptions
{
    /// <summary>Master switch for the SNMP polling background service.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hours between automatic SNMP polls of each configured printer.</summary>
    public int PollHours { get; set; } = 6;

    /// <summary>SNMP request timeout per printer, milliseconds.</summary>
    public int TimeoutMs { get; set; } = 4000;

    /// <summary>Max concurrent SNMP polls.</summary>
    public int MaxParallelism { get; set; } = 8;
}
