using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Services;

namespace PrintTrack.Server.Pages.Users;

public sealed class EditModel(AppDbContext db) : PageModel
{
    public bool IsNew => Input.Id == 0;
    public SelectList Departments { get; private set; } = new(Array.Empty<string>());

    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        public int Id { get; set; }
        [Required] public string UserName { get; set; } = "";
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public int? DepartmentId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        await LoadDeptsAsync();
        if (id is null) return Page();
        var u = await db.EndUsers.FindAsync(id);
        if (u is null) return NotFound();
        Input = new InputModel
        {
            Id = u.Id, UserName = u.UserName, FullName = u.FullName,
            Email = u.Email, DepartmentId = u.DepartmentId
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadDeptsAsync();
        if (!ModelState.IsValid) return Page();

        EndUser u;
        if (Input.Id == 0)
        {
            var norm = Naming.NormalizeUser(Input.UserName);
            if (await db.EndUsers.AnyAsync(x => x.NormalizedUserName == norm))
            {
                ModelState.AddModelError("Input.UserName", "Ya existe un usuario con ese login.");
                return Page();
            }
            u = new EndUser { UserName = Input.UserName, NormalizedUserName = norm };
            db.EndUsers.Add(u);
        }
        else
        {
            u = await db.EndUsers.FindAsync(Input.Id) ?? throw new InvalidOperationException();
        }

        u.FullName = Input.FullName;
        u.Email = Input.Email;
        u.DepartmentId = Input.DepartmentId;
        u.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        TempData["Msg"] = "Usuario guardado.";
        return RedirectToPage("Index");
    }

    private async Task LoadDeptsAsync() =>
        Departments = new SelectList(
            await db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name)
                .Select(d => new { d.Id, d.Name }).ToListAsync(),
            "Id", "Name");
}
