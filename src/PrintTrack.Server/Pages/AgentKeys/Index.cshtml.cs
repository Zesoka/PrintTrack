using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.AgentKeys;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public List<AgentApiKey> Keys { get; private set; } = [];
    public SelectList Sites { get; private set; } = new(Array.Empty<string>());

    /// <summary>Set once right after creation so the plaintext can be shown a single time.</summary>
    public string? NewPlaintextKey { get; private set; }

    /// <summary>Ready-to-paste install command for the just-created key.</summary>
    public string? NewInstallCommand { get; private set; }

    [BindProperty] [Required] public string NewKeyName { get; set; } = "";
    [BindProperty] public int? NewKeySiteId { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var plain = ApiKeyHashing.Generate();
        db.AgentApiKeys.Add(new AgentApiKey
        {
            Name = NewKeyName.Trim(),
            KeyHash = ApiKeyHashing.Hash(plain),
            Prefix = plain[..8],
            SiteId = NewKeySiteId,
            IsActive = true
        });
        await db.SaveChangesAsync();

        NewPlaintextKey = plain;
        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        NewInstallCommand =
            "Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force\r\n" +
            $".\\install-agent.ps1 -ServerUrl \"{serverUrl}\" -ApiKey \"{plain}\" -SourceDir \"C:\\Temp\\Agente\"";
        TempData["Msg"] = "Clave creada. Copiá el comando ahora: la clave no se vuelve a mostrar.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        var k = await db.AgentApiKeys.FindAsync(id);
        if (k is null) return NotFound();
        k.IsActive = false;
        await db.SaveChangesAsync();
        TempData["Msg"] = $"Clave '{k.Name}' revocada.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Keys = await db.AgentApiKeys.Include(k => k.Site)
            .OrderByDescending(k => k.CreatedAt).ToListAsync();
        Sites = new SelectList(
            await db.Sites.Where(s => s.IsActive).OrderBy(s => s.Name)
                .Select(s => new { s.Id, s.Name }).ToListAsync(),
            "Id", "Name");
    }
}
