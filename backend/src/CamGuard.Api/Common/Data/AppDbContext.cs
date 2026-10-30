using Microsoft.EntityFrameworkCore;

namespace CamGuard.Api.Common.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<Recording> Recordings => Set<Recording>();
    public DbSet<CameraEvent> CameraEvents => Set<CameraEvent>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<BatteryReading> BatteryReadings => Set<BatteryReading>();
    public DbSet<HealthAlert> HealthAlerts => Set<HealthAlert>();

    public Task<AppSettings> GetSettingsAsync(CancellationToken ct) =>
        Settings.SingleAsync(s => s.Id == AppSettings.SingletonId, ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Camera>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100);
            e.Property(c => c.Ip).HasMaxLength(64);
            e.Property(c => c.Go2rtcStream).HasMaxLength(100);
            e.Property(c => c.Username).HasMaxLength(64);
            e.Property(c => c.PasswordProtected).HasMaxLength(1000);
            e.Property(c => c.Model).HasMaxLength(100);
            e.Property(c => c.MotionSensitivity).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20).HasDefaultValue(CameraKind.Icsee).HasSentinel(CameraKind.Icsee);
            e.Property(c => c.TuyaDeviceId).HasMaxLength(64);
            e.Property(c => c.TuyaTriggerDeviceIds).HasMaxLength(400);
            e.Property(c => c.DetectionMode).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(c => c.Go2rtcStream).IsUnique();
            e.HasIndex(c => c.TuyaDeviceId).IsUnique();
        });

        modelBuilder.Entity<Recording>(e =>
        {
            e.Property(r => r.StorageKey).HasMaxLength(300);
            e.Property(r => r.ThumbnailKey).HasMaxLength(300);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Detection).HasMaxLength(50);
            e.HasIndex(r => r.StartedAt);
            e.HasIndex(r => new { r.CameraId, r.StartedAt });
            e.HasOne(r => r.Camera).WithMany().HasForeignKey(r => r.CameraId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CameraEvent>(e =>
        {
            e.Property(ev => ev.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(ev => ev.Detail).HasMaxLength(100);
            e.HasIndex(ev => ev.OccurredAt);
            e.HasOne<Camera>().WithMany().HasForeignKey(ev => ev.CameraId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppSettings>(e =>
        {
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.HealthAlertsEnabled).HasDefaultValue(true).HasSentinel(true);
            e.Property(s => s.LowBatteryPercent).HasDefaultValue(20);
            e.Property(s => s.SilentCameraHours).HasDefaultValue(48);
            e.HasData(new AppSettings());
        });

        modelBuilder.Entity<BatteryReading>(e =>
        {
            e.HasIndex(b => new { b.CameraId, b.At });
            e.HasOne<Camera>().WithMany().HasForeignKey(b => b.CameraId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HealthAlert>(e =>
        {
            e.HasKey(a => a.Key);
            e.Property(a => a.Key).HasMaxLength(100);
            e.Property(a => a.Title).HasMaxLength(200);
        });
    }
}
