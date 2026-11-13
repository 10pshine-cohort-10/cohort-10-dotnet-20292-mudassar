using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Api.Entities;

namespace TaskManager.Api.Data.Configurations;

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> e)
    {
        e.Property(t => t.Title).IsRequired().HasMaxLength(200);
        e.Property(t => t.Description).HasMaxLength(2000);
        e.Property(t => t.Status).HasConversion<int>();
        e.Property(t => t.Priority).HasConversion<int>();
        e.Property(t => t.RowVersion).IsRowVersion().IsRequired(false);

        // Soft-delete filter: deleted tasks stay in the database but are hidden from normal queries
        e.HasQueryFilter(t => !t.IsDeleted);

        e.HasIndex(t => new { t.AssignedToId, t.Status });
        e.HasIndex(t => t.DueDate);
        e.HasIndex(t => t.CategoryId);
        e.HasIndex(t => t.IsDeleted);

        e.HasOne(t => t.CreatedBy)
            .WithMany(u => u.CreatedTasks)
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(t => t.AssignedTo)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(t => t.AssignedToId)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(t => t.Category)
            .WithMany(c => c.Tasks)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

