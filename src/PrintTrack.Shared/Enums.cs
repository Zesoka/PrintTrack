namespace PrintTrack.Shared;

/// <summary>Detected color usage of a print job.</summary>
public enum ColorMode
{
    Unknown = 0,
    Grayscale = 1,
    Color = 2
}

/// <summary>Detected duplex (double-sided) usage of a print job.</summary>
public enum DuplexMode
{
    Unknown = 0,
    Simplex = 1,
    Duplex = 2
}

/// <summary>What the agent must do with a job after asking the server.</summary>
public enum JobDecisionType
{
    /// <summary>Release the job to the printer.</summary>
    Allow = 0,

    /// <summary>Delete the job from the queue (user blocked / printer disabled).</summary>
    Deny = 1
}

/// <summary>Lifecycle state persisted for every observed job.</summary>
public enum PrintJobStatus
{
    Pending = 0,
    Printed = 2,
    Denied = 3,
    Cancelled = 5,
    Failed = 6
}
