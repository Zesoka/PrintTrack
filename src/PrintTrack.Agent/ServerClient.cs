using System.Net;
using System.Net.Http.Json;
using PrintTrack.Shared;

namespace PrintTrack.Agent;

/// <summary>Typed HTTP client for the server's agent API.</summary>
public sealed class ServerClient(HttpClient http, ILogger<ServerClient> logger)
{
    public async Task<AuthorizeJobResponse?> AuthorizeAsync(AuthorizeJobRequest req, CancellationToken ct)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("/api/agent/jobs/authorize", req, ct);
            if (resp.StatusCode == HttpStatusCode.BadRequest)
            {
                logger.LogWarning("Autorización rechazada (400): {Body}", await resp.Content.ReadAsStringAsync(ct));
                return null;
            }
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<AuthorizeJobResponse>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo contactar al servidor para autorizar {JobRef}.", req.JobRef);
            return null;
        }
    }

    /// <returns>true if the server acknowledged; false to retry later.</returns>
    public async Task<bool> CompleteAsync(CompleteJobRequest req, CancellationToken ct)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("/api/agent/jobs/complete", req, ct);
            resp.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo al reportar el cierre de {JobRef} (se reintentará).", req.JobRef);
            return false;
        }
    }

    public async Task HeartbeatAsync(AgentHeartbeatRequest req, CancellationToken ct)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("/api/agent/heartbeat", req, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Heartbeat falló.");
        }
    }
}
