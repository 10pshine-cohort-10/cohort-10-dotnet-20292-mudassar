using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Api.Entities;

namespace TaskManager.Api.Data.Configurations;

public class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> e)
    {
        e.Property(c => c.Text).IsRequired().HasMaxLength(1000);
        e.HasQueryFilter(c => !c.TaskItem.IsDeleted);

        e.HasOne(c => c.TaskItem)
            .WithMany(t => t.Comments)
            .HasForeignKey(c => c.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);

        e.HasOne(c => c.User)
            .WithMany(u => u.TaskComments)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

