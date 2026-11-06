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
            builder
                .HasDiscriminator(x => x.Kind)
                .HasValue<OrganizationOwner>(OwnerKind.Organization)
                .HasValue<UserOwner>(OwnerKind.User);
            builder.ToTable(t => t.HasCheckConstraint(
                "ck_owners_kind_id",
                $"(kind = {(int)OwnerKind.Organization} and organization_id is not null and user_id is null) " +
                $"or (kind = {(int)OwnerKind.User} and user_id is not null and organization_id is null)"));
        });

        // Unique per service; filtered, since the other kind's rows have null there. FilesService.LockOwner's
        // "on conflict" names these columns and filters.
        modelBuilder.Entity<OrganizationOwner>(builder =>
        {
            builder
                .HasIndex(x => new { x.ServiceId, x.OrganizationId })
                .IsUnique()
                .HasFilter("organization_id is not null");
        });

        modelBuilder.Entity<UserOwner>(builder =>
        {
            builder
                .HasIndex(x => new { x.ServiceId, x.UserId })
                .IsUnique()
                .HasFilter("user_id is not null");
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
