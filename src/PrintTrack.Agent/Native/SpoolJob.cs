using PrintTrack.Shared;

namespace PrintTrack.Agent.Native;

/// <summary>Managed snapshot of one spooler job, decoded from JOB_INFO_2 + its DEVMODE.</summary>
public sealed record SpoolJob
{
    public required uint JobId { get; init; }
    public required string PrinterName { get; init; }
    public string? MachineName { get; init; }
    public required string UserName { get; init; }
    public required string Document { get; init; }
    public string? Datatype { get; init; }

    public uint StatusFlags { get; init; }
    public int TotalPages { get; init; }
    public int PagesPrinted { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset SubmittedUtc { get; init; }

    public int Copies { get; init; } = 1;
    public ColorMode Color { get; init; } = ColorMode.Unknown;
    public DuplexMode Duplex { get; init; } = DuplexMode.Unknown;
    public string? PaperSize { get; init; }

    public bool IsPaused => (StatusFlags & WinSpool.JOB_STATUS_PAUSED) != 0;
    public bool IsPrinting => (StatusFlags & WinSpool.JOB_STATUS_PRINTING) != 0;
    public bool IsSpooling => (StatusFlags & WinSpool.JOB_STATUS_SPOOLING) != 0;
    public bool IsPrinted => (StatusFlags & (WinSpool.JOB_STATUS_PRINTED | WinSpool.JOB_STATUS_COMPLETE)) != 0;
    public bool IsDeleted => (StatusFlags & (WinSpool.JOB_STATUS_DELETED | WinSpool.JOB_STATUS_DELETING)) != 0;
    public bool IsError => (StatusFlags & (WinSpool.JOB_STATUS_ERROR | WinSpool.JOB_STATUS_BLOCKED_DEVQ)) != 0;
}
