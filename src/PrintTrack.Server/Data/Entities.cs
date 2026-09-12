using Microsoft.AspNetCore.Identity;
using PrintTrack.Shared;

namespace PrintTrack.Server.Data;

/// <summary>Dashboard administrator (ASP.NET Core Identity).</summary>
public sealed class AdminUser : IdentityUser
{
    public string? DisplayName { get; set; }
}

/// <summary>Roles an <see cref="AdminUser"/> can hold (seeded once via DbSeeder).</summary>
public static class AdminRoles
{
    /// <summary>Sees and manages everything — all Sedes, catálogos, descubrimiento, otros admins.</summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Lectura y escritura, pero solo dentro de sus Sedes asignadas.</summary>
    public const string SedeAdmin = "SedeAdmin";

    /// <summary>Solo lectura, solo dentro de sus Sedes asignadas.</summary>
    public const string SedeViewer = "SedeViewer";
}

/// <summary>Which Sedes a non-SuperAdmin <see cref="AdminUser"/> can see/manage. Irrelevant for
/// SuperAdmin, who sees every Sede regardless of rows here.</summary>
public sealed class AdminSiteAccess
{
    public int Id { get; set; }
    public string AdminUserId { get; set; } = "";
    public AdminUser AdminUser { get; set; } = null!;
    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;
}

/// <summary>A branch / location. Assigned to a printer; every job from that printer's log inherits it.</summary>
public sealed class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

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

    /// <summary>Record jobs from this device's log. If false the device is kept but its jobs aren't imported.</summary>
    public bool IsTracked { get; set; } = true;

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

    /// <summary>Branch this job's printer sits at (copied from the printer when the job is imported).</summary>
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

    /// <summary>Dedup key: hash of printer + timestamp + user + document. Unique per printer.</summary>
    public string? ExternalId { get; set; }

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? DecisionMessage { get; set; }
}
