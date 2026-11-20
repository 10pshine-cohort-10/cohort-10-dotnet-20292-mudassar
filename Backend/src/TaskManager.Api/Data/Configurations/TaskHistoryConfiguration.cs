using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Api.Entities;

namespace TaskManager.Api.Data.Configurations;

public class TaskHistoryConfiguration : IEntityTypeConfiguration<TaskHistory>
{
    public void Configure(EntityTypeBuilder<TaskHistory> builder)
    {
        builder.Property(h => h.Action).IsRequired().HasMaxLength(100);
        builder.Property(h => h.OldValue).HasMaxLength(1000);
        builder.Property(h => h.NewValue).HasMaxLength(1000);
        builder.HasQueryFilter(h => !h.TaskItem.IsDeleted);

        builder.HasOne(h => h.TaskItem)
            .WithMany(t => t.History)
            .HasForeignKey(h => h.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.User)
            .WithMany(u => u.TaskHistories)
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

