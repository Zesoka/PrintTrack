using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PrintTrack.Server.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AdminUser>(options)
{
    public DbSet<EndUser> EndUsers => Set<EndUser>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<PrintJobRecord> PrintJobs => Set<PrintJobRecord>();
    public DbSet<AgentApiKey> AgentApiKeys => Set<AgentApiKey>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<PrinterMeterConfig> PrinterMeterConfigs => Set<PrinterMeterConfig>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<PrinterJobLogConfig> PrinterJobLogConfigs => Set<PrinterJobLogConfig>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Site>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Code).HasMaxLength(32);
        });

        b.Entity<Department>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Code).HasMaxLength(32);
        });

        b.Entity<EndUser>(e =>
        {
            e.HasIndex(x => x.NormalizedUserName).IsUnique();
            e.Property(x => x.UserName).HasMaxLength(256);
            e.Property(x => x.NormalizedUserName).HasMaxLength(256);
            e.HasOne(x => x.Department).WithMany(d => d.Users)
                .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Printer>(e =>
        {
            e.HasIndex(x => new { x.Name, x.WorkstationName }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.WorkstationName).HasMaxLength(256);
            e.HasOne(x => x.Site).WithMany()
                .HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PrintJobRecord>(e =>
        {
            e.HasIndex(x => x.JobRef).IsUnique();
            e.HasIndex(x => x.SubmittedAt);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SiteId);
            e.HasIndex(x => new { x.PrinterId, x.ExternalId }).IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");
            e.Property(x => x.DocumentName).HasMaxLength(512);
            e.HasOne(x => x.EndUser).WithMany(u => u.Jobs)
                .HasForeignKey(x => x.EndUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Printer).WithMany(p => p.Jobs)
                .HasForeignKey(x => x.PrinterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Site).WithMany(s => s.Jobs)
                .HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AgentApiKey>(e =>
        {
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.Property(x => x.KeyHash).HasMaxLength(64);
            e.Property(x => x.Prefix).HasMaxLength(16);
            e.HasOne(x => x.Site).WithMany(s => s.AgentKeys)
                .HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PrinterMeterConfig>(e =>
        {
            e.HasKey(x => x.PrinterId);
            e.HasOne(x => x.Printer).WithOne()
                .HasForeignKey<PrinterMeterConfig>(x => x.PrinterId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Host).HasMaxLength(256);
            e.Property(x => x.Community).HasMaxLength(128);
            e.Property(x => x.OidTotal).HasMaxLength(256);
            e.Property(x => x.DeviceName).HasMaxLength(256);
        });

        b.Entity<MeterReading>(e =>
        {
            e.HasIndex(x => new { x.PrinterId, x.TakenAt });
            e.HasOne(x => x.Printer).WithMany()
                .HasForeignKey(x => x.PrinterId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PrinterJobLogConfig>(e =>
        {
            e.HasKey(x => x.PrinterId);
            e.HasOne(x => x.Printer).WithOne()
                .HasForeignKey<PrinterJobLogConfig>(x => x.PrinterId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.BaseUrl).HasMaxLength(256);
            e.Property(x => x.ReportPath).HasMaxLength(256);
            e.Property(x => x.ExportFormatId).HasMaxLength(64);
        });
    }
}
