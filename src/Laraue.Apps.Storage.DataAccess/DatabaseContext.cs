using Laraue.Apps.Storage.DataAccess.Data;
using Laraue.Apps.Storage.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Storage.DataAccess;

public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions options)
        : base(options)
    {
    }

    public required DbSet<Service> Services { get; set; }
    public required DbSet<Owner> Owners { get; set; }
    public required DbSet<StoredFile> StoredFiles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Service>()
            .HasData(ServicesData.Services);

        modelBuilder.Entity<Owner>(builder =>
        {
            builder.HasIndex(x => new { x.ServiceId, x.Key }).IsUnique();
        });

        modelBuilder.Entity<StoredFile>(builder =>
        {
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder
                .HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
            builder
                .HasOne(x => x.SourceFile)
                .WithOne(x => x.Preview)
                .HasForeignKey<StoredFile>(x => x.SourceFileId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => new { x.OwnerId, x.ContentHash });
        });
    }
}
