using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;
using PrintTrack.Shared;

namespace PrintTrack.Server.Api;

public static class AgentEndpoints
{
    public static void MapAgentApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/agent").RequireAuthorization(ApiKeyDefaults.Policy);

        g.MapPost("/jobs/authorize", async (AuthorizeJobRequest req, HttpContext http, AuditService audit, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.JobRef) || string.IsNullOrWhiteSpace(req.UserName)
                || string.IsNullOrWhiteSpace(req.PrinterName))
                return Results.BadRequest("JobRef, UserName y PrinterName son obligatorios.");

            int? siteId = int.TryParse(http.User.FindFirst("agent_site_id")?.Value, out var s) ? s : null;
            return Results.Ok(await audit.AuthorizeJobAsync(req, siteId, ct));
        });

        g.MapPost("/jobs/complete", async (CompleteJobRequest req, AuditService audit, CancellationToken ct) =>
        {
            await audit.CompleteJobAsync(req, ct);
            return Results.NoContent();
        });

        g.MapPost("/heartbeat", async (AgentHeartbeatRequest req, AppDbContext db, CancellationToken ct) =>
        {
            foreach (var name in req.Printers.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var p = await db.Printers.FirstOrDefaultAsync(
                    x => x.Name == name && x.WorkstationName == req.WorkstationName, ct);
                if (p is null)
                {
                    db.Printers.Add(new Printer
                    {
                        Name = name,
                        WorkstationName = req.WorkstationName,
                        FirstSeenAt = DateTimeOffset.UtcNow,
                        LastSeenAt = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    p.LastSeenAt = DateTimeOffset.UtcNow;
                }
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
