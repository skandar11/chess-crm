using ChessCrm.MigrationTool.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChessCrm.MigrationTool.Database.Configurations;

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.Property(g => g.Level).HasConversion<string>();
        builder.Property(g => g.Format).HasConversion<string>();
    }
}