using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Api.Entities;

namespace TaskManager.Api.Data.Configurations;

public class TaskHistoryConfiguration : IEntityTypeConfiguration<TaskHistory>
{
    public void Configure(EntityTypeBuilder<TaskHistory> e)
    {
        e.Property(h => h.Action).IsRequired().HasMaxLength(100);
        e.Property(h => h.OldValue).HasMaxLength(1000);
        e.Property(h => h.NewValue).HasMaxLength(1000);
        e.HasQueryFilter(h => !h.TaskItem.IsDeleted);

        e.HasOne(h => h.TaskItem)
            .WithMany(t => t.History)
            .HasForeignKey(h => h.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(h => h.User)
            .WithMany(u => u.TaskHistories)
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

