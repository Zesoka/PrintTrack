namespace PrintTrack.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Base URL of the server, e.g. <c>https://auditor.miempresa.local</c>.</summary>
    public string ServerUrl { get; set; } = "http://localhost:8095";

    /// <summary>Agent API key issued in the dashboard (sent as <c>X-Api-Key</c>).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Queues to watch. Empty = every local queue returned by the spooler.</summary>
    public string[] Printers { get; set; } = [];

    /// <summary>Queues to never watch (e.g. "Microsoft Print to PDF").</summary>
    public string[] IgnorePrinters { get; set; } =
        ["Microsoft Print to PDF", "Microsoft XPS Document Writer", "Fax", "OneNote", "Send To OneNote"];

    /// <summary>Max time to wait for the server before applying <see cref="ServerUnreachable"/>.</summary>
    public int ServerTimeoutSeconds { get; set; } = 8;

    /// <summary>What to do with a job when the server can't be reached at authorization time.</summary>
    public FailMode ServerUnreachable { get; set; } = FailMode.Allow;

    /// <summary>How often to re-send the heartbeat / re-scan local queues.</summary>
    public int HeartbeatMinutes { get; set; } = 15;

    /// <summary>Poll interval for the spooler when change notifications are quiet (safety net).</summary>
    public int PollSeconds { get; set; } = 10;
}

public enum FailMode
{
    /// <summary>Release the job (printing keeps working).</summary>
    Allow = 0,

    /// <summary>Delete the job.</summary>
    Deny = 1
}
