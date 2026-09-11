using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrintTrack.Server.Options;

namespace PrintTrack.Server.Data;

public static class DbSeeder
{
    public static async Task MigrateAndSeedAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var s = scope.ServiceProvider;
        var db = s.GetRequiredService<AppDbContext>();
        var opt = s.GetRequiredService<IOptions<PrintTrackOptions>>().Value;
        var log = s.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        // On a host/Docker restart the DB container may not be resolvable/accepting connections yet
        // when this runs. Retry instead of crashing the whole process.
        await WaitForDatabaseAsync(db, log);
        await db.Database.MigrateAsync();

        var users = s.GetRequiredService<UserManager<AdminUser>>();
        if (!await users.Users.AnyAsync())
        {
            var admin = new AdminUser
            {
                UserName = opt.SeedAdminUser,
                Email = opt.SeedAdminEmail,
                DisplayName = "Administrador",
                EmailConfirmed = true
            };
            var res = await users.CreateAsync(admin, opt.SeedAdminPassword);
            if (res.Succeeded)
                log.LogWarning("Admin '{User}' creado con la contraseña semilla. Cámbiela cuanto antes.", opt.SeedAdminUser);
            else
                log.LogError("No se pudo crear el admin semilla: {Errors}",
                    string.Join("; ", res.Errors.Select(e => e.Description)));
        }
    }

    private static async Task WaitForDatabaseAsync(AppDbContext db, ILogger log)
    {
        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (await db.Database.CanConnectAsync()) return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                log.LogInformation("Base de datos aún no disponible (intento {Attempt}/{Max}): {Msg}",
                    attempt, maxAttempts, ex.Message);
            }

            if (attempt >= maxAttempts)
                throw new InvalidOperationException(
                    $"No se pudo conectar a la base de datos tras {maxAttempts} intentos.");

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
