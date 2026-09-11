using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrintTrack.Server.Data;
using PrintTrack.Server.Options;
using PrintTrack.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PrintTrackOptions>(builder.Configuration.GetSection(PrintTrackOptions.SectionName));

var connString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=printtrack;Username=printtrack;Password=printtrack";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connString));

// Persist the DataProtection key ring so cookies / antiforgery tokens survive container restarts.
// The path is a mounted volume in docker-compose; falls back to the default location if absent.
var keyRing = builder.Configuration["DataProtection:KeyPath"]
    ?? (Directory.Exists("/keys") ? "/keys" : null);
var dp = builder.Services.AddDataProtection().SetApplicationName("auditor-impresiones");
if (keyRing is not null)
    dp.PersistKeysToFileSystem(new DirectoryInfo(keyRing));

builder.Services.AddIdentity<AdminUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = false;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/Login";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

builder.Services.AddScoped<HpJobLogImporter>();

builder.Services.AddSingleton<SnmpMeterReader>();
builder.Services.AddSingleton<MeterPollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MeterPollingService>());

builder.Services.AddSingleton<JobLogFetcher>();
builder.Services.AddSingleton<JobLogPollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobLogPollingService>());

builder.Services.AddSingleton<IppClient>();

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToPage("/Account/Login");
    o.Conventions.AllowAnonymousToPage("/Account/Logout");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();

await DbSeeder.MigrateAndSeedAsync(app.Services);

app.Run();
