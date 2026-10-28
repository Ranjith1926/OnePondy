using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.TripPlanner;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

public sealed class TripPlannerRequestLogConfiguration : IEntityTypeConfiguration<TripPlannerRequestLog>
{
    public void Configure(EntityTypeBuilder<TripPlannerRequestLog> builder)
    {
        builder.ToTable("trip_planner_request_logs", table =>
            table.HasCheckConstraint("ck_trip_planner_log_outcome", $"outcome IN ({SnakeCaseEnum<TripPlannerOutcome>.SqlList})"));

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Interests).HasMaxLength(500).IsRequired();
        builder.Property(l => l.EstimatedCostUsd).HasPrecision(10, 4);
        builder.Property(l => l.Outcome).HasConversion(SnakeCaseEnum<TripPlannerOutcome>.Converter).HasMaxLength(24);

        // Both the daily cost cap and the per-user daily limit query "today's rows"; a composite
        // index on (created_at, user_id) serves both without a second index.
        builder.HasIndex(l => new { l.CreatedAt, l.UserId });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
