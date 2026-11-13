using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Api.Entities;

namespace TaskManager.Api.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> e)
    {
        e.Property(c => c.Name).IsRequired().HasMaxLength(100);
        e.Property(c => c.Description).HasMaxLength(500);
        e.HasIndex(c => c.Name).IsUnique();
    }
}

