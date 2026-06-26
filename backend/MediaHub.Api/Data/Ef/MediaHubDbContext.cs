using MediaHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaHub.Api.Data.Ef;

/// <summary>
/// EF Core context for the self-hosted SQL providers (SQLite / PostgreSQL / MySQL /
/// SQL Server). Maps the same logical entities as the D1 path to the same table and
/// column names, so the two backends are interchangeable. Schema is created with
/// <c>EnsureCreated()</c> (additive; no destructive migrations).
/// </summary>
public sealed class MediaHubDbContext(DbContextOptions<MediaHubDbContext> options) : DbContext(options)
{
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<AppRelease> AppReleases => Set<AppRelease>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<AppConfigEntry> AppConfig => Set<AppConfigEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Video>(e =>
        {
            e.ToTable("videos");
            e.HasKey(v => v.Id);
            e.Property(v => v.Id).HasColumnName("id");
            e.Property(v => v.Title).HasColumnName("title").IsRequired();
            e.Property(v => v.Description).HasColumnName("description");
            e.Property(v => v.ObjectKey).HasColumnName("object_key").IsRequired();
            e.Property(v => v.ThumbnailUrl).HasColumnName("thumbnail_url");
            e.Property(v => v.DurationSeconds).HasColumnName("duration_seconds");
            e.Property(v => v.MimeType).HasColumnName("mime_type").IsRequired();
            e.Property(v => v.CreatedAt).HasColumnName("created_at");
            e.HasIndex(v => v.CreatedAt).HasDatabaseName("ix_videos_created_at");
        });

        b.Entity<AppRelease>(e =>
        {
            e.ToTable("app_releases");
            e.HasKey(r => r.VersionCode);
            e.Property(r => r.VersionCode).HasColumnName("version_code").ValueGeneratedNever();
            e.Property(r => r.VersionName).HasColumnName("version_name").IsRequired();
            e.Property(r => r.Notes).HasColumnName("notes");
            e.Property(r => r.ObjectKey).HasColumnName("object_key").IsRequired();
            e.Property(r => r.SizeBytes).HasColumnName("size_bytes");
            e.Property(r => r.Sha256).HasColumnName("sha256").IsRequired();
            e.Property(r => r.MinSdk).HasColumnName("min_sdk");
            e.Property(r => r.PublishedAt).HasColumnName("published_at");
        });

        b.Entity<Admin>(e =>
        {
            e.ToTable("admins");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.Username).HasColumnName("username").IsRequired();
            e.HasIndex(a => a.Username).IsUnique().HasDatabaseName("ux_admins_username");
            e.Property(a => a.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(a => a.PasswordSalt).HasColumnName("password_salt").IsRequired();
            e.Property(a => a.CreatedAt).HasColumnName("created_at");
        });

        b.Entity<AppConfigEntry>(e =>
        {
            e.ToTable("app_config");
            e.HasKey(c => c.Key);
            e.Property(c => c.Key).HasColumnName("key");
            e.Property(c => c.Value).HasColumnName("value").IsRequired();
        });
    }
}
