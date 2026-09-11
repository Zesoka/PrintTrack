using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrintTrack.Server.Data;

namespace PrintTrack.Server.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(SignInManager<AdminUser> signInManager) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; set; }

    public sealed class InputModel
    {
        [Required] public string UserName { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        if (!ModelState.IsValid) return Page();

        var result = await signInManager.PasswordSignInAsync(
            Input.UserName, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded) return LocalRedirect(returnUrl);
        if (result.IsLockedOut) ModelState.AddModelError(string.Empty, "Cuenta bloqueada temporalmente. Intente más tarde.");
        else ModelState.AddModelError(string.Empty, "Usuario o contraseña incorrectos.");
        return Page();
    }
}
