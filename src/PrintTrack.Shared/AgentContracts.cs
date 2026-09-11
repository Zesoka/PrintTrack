namespace PrintTrack.Shared;

/// <summary>
/// Sent by an agent the moment a new job is detected in the local spooler. The server replies
/// with an <see cref="AuthorizeJobResponse"/> telling the agent to release or delete the job.
/// </summary>
public sealed record AuthorizeJobRequest
{
    /// <summary>Stable id the agent assigns to this job (GUID). Used for all follow-up calls.</summary>
    public required string JobRef { get; init; }

    /// <summary>Windows account that owns the job, e.g. <c>DOMAIN\\jdoe</c> or <c>jdoe</c>.</summary>
    public required string UserName { get; init; }

    /// <summary>Machine the job was submitted from.</summary>
    public required string WorkstationName { get; init; }

    /// <summary>Local spooler queue name.</summary>
    public required string PrinterName { get; init; }

    /// <summary>Optional shared/UNC name if this is a server queue (<c>\\\\SRV\\HP2</c>).</summary>
    public string? PrinterShareName { get; init; }

    public required string DocumentName { get; init; }

    /// <summary>Pages in one copy of the document (from the driver / spool file). 0 if unknown.</summary>
    public int Pages { get; init; }

    public int Copies { get; init; } = 1;

    public ColorMode Color { get; init; } = ColorMode.Unknown;

    public DuplexMode Duplex { get; init; } = DuplexMode.Unknown;

    /// <summary>Paper size name as reported by the driver, e.g. <c>A4</c>, <c>Legal</c>.</summary>
    public string? PaperSize { get; init; }

    /// <summary>Raw spool size in bytes, for diagnostics.</summary>
    public long SizeBytes { get; init; }

    public DateTimeOffset SubmittedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AuthorizeJobResponse
{
    public required JobDecisionType Decision { get; init; }

    /// <summary>Sheets the server recorded (pages * copies).</summary>
    public int Sheets { get; init; }

    /// <summary>Human-readable reason, shown in the deny toast / logged.</summary>
    public string? Message { get; init; }
}

/// <summary>Sent once the job's final outcome is known (printed, cancelled, failed).</summary>
public sealed record CompleteJobRequest
{
    public required string JobRef { get; init; }
    public required PrintJobStatus Status { get; init; }

    /// <summary>Actual sheets printed if the spooler reported a different number than estimated.</summary>
    public int? ActualPages { get; init; }

    public string? Note { get; init; }
}

/// <summary>Agent announces itself + the local queues it manages on startup / heartbeat.</summary>
public sealed record AgentHeartbeatRequest
{
    public required string WorkstationName { get; init; }
    public required string AgentVersion { get; init; }
    public required IReadOnlyList<string> Printers { get; init; }
    public string? OsVersion { get; init; }
}
