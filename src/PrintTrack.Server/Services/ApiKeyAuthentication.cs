using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Services;

public static class ApiKeyDefaults
{
    public const string Scheme = "AgentApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string Policy = "AgentOnly";
}

public static class ApiKeyHashing
{
    public static string Hash(string plaintext)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();

    /// <summary>Generate a new agent key: <c>pt_</c> + 40 url-safe chars.</summary>
    public static string Generate()
    {
        Span<byte> buf = stackalloc byte[30];
        RandomNumberGenerator.Fill(buf);
        return "pt_" + Convert.ToBase64String(buf).Replace('+', 'A').Replace('/', 'B').TrimEnd('=');
    }
}

public sealed class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var provided) || provided.Count == 0)
            return AuthenticateResult.NoResult();

        var hash = ApiKeyHashing.Hash(provided.ToString().Trim());
        var key = await db.AgentApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive);
        if (key is null)
            return AuthenticateResult.Fail("Clave de agente inválida o revocada.");

        key.LastUsedAt = DateTimeOffset.UtcNow;
        if (Request.Headers.TryGetValue("X-Workstation", out var ws) && ws.Count > 0)
            key.LastUsedFromWorkstation = ws.ToString();
        await db.SaveChangesAsync();

        var identity = new ClaimsIdentity(ApiKeyDefaults.Scheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, key.Name));
        identity.AddClaim(new Claim("agent_key_id", key.Id.ToString()));
        if (key.SiteId is int siteId)
            identity.AddClaim(new Claim("agent_site_id", siteId.ToString()));
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyDefaults.Scheme));
    }
}
