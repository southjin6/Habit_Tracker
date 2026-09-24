using HabitTracker.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace HabitTracker.Core.Data;

public class HabitDbContext : DbContext
{
    public HabitDbContext(DbContextOptions<HabitDbContext> options)
        : base(options)
    {
    }

    public DbSet<Item> Items => Set<Item>();

    public DbSet<Completion> Completions => Set<Completion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(entity =>
        {
            entity.ToTable("Items");
            entity.HasKey(i => i.Id);

            entity.Property(i => i.Name).IsRequired().HasMaxLength(Item.NameMaxLength);
            entity.Property(i => i.Kind).HasConversion<byte>();
            entity.Property(i => i.CreatedOn).HasColumnType("date");
            entity.Property(i => i.CompletedOn).HasColumnType("date");

            entity.Ignore(i => i.IsDaily);
            entity.Ignore(i => i.IsTask);

            // Cascade so deleting a daily takes its completion history with it, leaving no orphans.
            entity.HasMany(i => i.Completions)
                  .WithOne(c => c.Item!)
                  .HasForeignKey(c => c.ItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Completion>(entity =>
        {
            entity.ToTable("Completions");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Date).HasColumnType("date");

            // One completion per daily per day, enforced by the database itself rather than only by
            // application code, so a double-mark can never advance a streak twice.
            entity.HasIndex(c => new { c.ItemId, c.Date })
                  .IsUnique()
                  .HasDatabaseName("UX_Completions_ItemId_Date");
        });
    }
}
