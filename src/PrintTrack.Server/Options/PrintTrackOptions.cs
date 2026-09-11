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

    /// <summary>Plaintext bootstrap agent key seeded on first run (blank = none, create in UI).</summary>
    public string? SeedAgentApiKey { get; set; }

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
