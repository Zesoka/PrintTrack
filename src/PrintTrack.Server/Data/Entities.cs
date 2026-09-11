using Microsoft.AspNetCore.Identity;
using PrintTrack.Shared;

namespace PrintTrack.Server.Data;

/// <summary>Dashboard administrator (ASP.NET Core Identity).</summary>
public sealed class AdminUser : IdentityUser
{
    public string? DisplayName { get; set; }
}

/// <summary>A branch / location. Assigned to an agent key; every job that key reports inherits it.</summary>
public sealed class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    public List<AgentApiKey> AgentKeys { get; set; } = [];
    public List<PrintJobRecord> Jobs { get; set; } = [];
}

/// <summary>An area / department. Assigned to a user.</summary>
public sealed class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    public List<EndUser> Users { get; set; } = [];
}

public sealed class EndUser
{
    public int Id { get; set; }

    /// <summary>Login as reported by the agent (may include domain).</summary>
    public string UserName { get; set; } = "";

    /// <summary>Upper-cased, domain-stripped key used for matching.</summary>
    public string NormalizedUserName { get; set; } = "";

    public string? FullName { get; set; }
    public string? Email { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>All printing denied for this user.</summary>
    public bool IsBlocked { get; set; }

    /// <summary>Row was created automatically the first time this login printed.</summary>
    public bool AutoCreated { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }

    public List<PrintJobRecord> Jobs { get; set; } = [];
}

public sealed class Printer
{
    public int Id { get; set; }

    /// <summary>Spooler queue name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Null for centralized server queues; set for a workstation's local printer.</summary>
    public string? WorkstationName { get; set; }

    public string? ShareName { get; set; }
    public string? Location { get; set; }

    /// <summary>Branch this device physically sits at. Used to tag jobs imported from its job log.</summary>
    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    /// <summary>Record jobs sent here. If false the job is allowed but not logged.</summary>
    public bool IsTracked { get; set; } = true;

    /// <summary>Deny every job sent here.</summary>
    public bool IsDisabled { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PrintJobRecord> Jobs { get; set; } = [];
}

public sealed class PrintJobRecord
{
    public long Id { get; set; }
    public string JobRef { get; set; } = "";

    public int EndUserId { get; set; }
    public EndUser EndUser { get; set; } = null!;
    public string UserNameRaw { get; set; } = "";

    public int PrinterId { get; set; }
    public Printer Printer { get; set; } = null!;
    public string PrinterNameRaw { get; set; } = "";
    public string? WorkstationName { get; set; }

    /// <summary>Branch the reporting agent belongs to (from its API key), stamped at authorize time.</summary>
    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public string DocumentName { get; set; } = "";

    /// <summary>Pages in one copy.</summary>
    public int Pages { get; set; }
    public int Copies { get; set; } = 1;

    /// <summary>Total sheets = pages * copies (what actually comes out of the printer).</summary>
    public int Sheets { get; set; }

    public ColorMode Color { get; set; }
    public DuplexMode Duplex { get; set; }
    public string? PaperSize { get; set; }
    public long SizeBytes { get; set; }

    public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;

    /// <summary>Where this record came from — an agent, or the HP device job-log import.</summary>
    public JobSource Source { get; set; } = JobSource.Agent;

    /// <summary>Dedup key for imported records (null for agent jobs). Unique per printer.</summary>
    public string? ExternalId { get; set; }

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? DecisionMessage { get; set; }
}

public enum JobSource { Agent = 0, HpJobLog = 1 }

public sealed class AgentApiKey
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>SHA-256 (hex) of the full key. The plaintext is shown once at creation.</summary>
    public string KeyHash { get; set; } = "";

    /// <summary>First 8 chars of the plaintext, for identification in the UI.</summary>
    public string Prefix { get; set; } = "";

    /// <summary>The branch this key belongs to. Jobs reported with it inherit this site.</summary>
    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LastUsedFromWorkstation { get; set; }
}
