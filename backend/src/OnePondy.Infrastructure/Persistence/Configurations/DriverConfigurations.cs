using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Food;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Delivery drivers (spec Phase 8, step 8.6). The rules the model states are also CHECK constraints, so no code path can break them.

public sealed class DriverProfileConfiguration : IEntityTypeConfiguration<DriverProfile>
{
    public void Configure(EntityTypeBuilder<DriverProfile> builder)
    {
        builder.ToTable("driver_profiles", table =>
        {
            table.HasCheckConstraint("ck_driver_vehicle_type", $"vehicle_type IN ({SnakeCaseEnum<VehicleType>.SqlList})");
            table.HasCheckConstraint("ck_driver_approval_status", $"approval_status IN ({SnakeCaseEnum<DriverApprovalStatus>.SqlList})");

            // Only an approved driver is ever on duty, and "on duty since" exists exactly while on duty.
            table.HasCheckConstraint("ck_driver_duty_requires_approval", "NOT is_on_duty OR approval_status = 'approved'");
            table.HasCheckConstraint("ck_driver_duty_since", "is_on_duty = (on_duty_since IS NOT NULL)");

            // A motorised vehicle has its registration and licence on file; a bicycle has neither.
            table.HasCheckConstraint(
                "ck_driver_documents",
                "(vehicle_type = 'bicycle' AND vehicle_number IS NULL AND licence_number IS NULL) "
                + "OR (vehicle_type <> 'bicycle' AND vehicle_number IS NOT NULL AND licence_number IS NOT NULL)");

            // A rejection or suspension always says why.
            table.HasCheckConstraint(
                "ck_driver_review_note",
                "approval_status NOT IN ('rejected', 'suspended') OR review_note IS NOT NULL");
        });

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).ValueGeneratedNever();
        builder.Property(p => p.FullName).HasMaxLength(DriverProfile.MaxNameLength).IsRequired();
        builder.Property(p => p.VehicleType).HasConversion(SnakeCaseEnum<VehicleType>.Converter).HasMaxLength(16);
        builder.Property(p => p.VehicleNumber).HasMaxLength(DriverProfile.MaxVehicleNumberLength);
        builder.Property(p => p.LicenceNumber).HasMaxLength(DriverProfile.MaxLicenceLength);
        builder.Property(p => p.ApprovalStatus).HasConversion(SnakeCaseEnum<DriverApprovalStatus>.Converter).HasMaxLength(16);
        builder.Property(p => p.ReviewNote).HasMaxLength(DriverProfile.MaxReasonLength);
        builder.Property(p => p.Version).IsRowVersion();

        // The account is anonymised, never deleted, so this never cascades: deleting an account removes its profile explicitly.
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<DriverProfile>(p => p.UserId).OnDelete(DeleteBehavior.Restrict);

        // One licence, one account: the same person cannot hold several driver accounts.
        builder.HasIndex(p => p.LicenceNumber).IsUnique().HasDatabaseName("ux_driver_profiles_licence").HasFilter("licence_number IS NOT NULL");

        // What dispatch and the review queue scan.
        builder.HasIndex(p => new { p.ApprovalStatus, p.IsOnDuty }).HasDatabaseName("ix_driver_profiles_status_duty");
    }
}

// Delivering food orders (spec Phase 8, step 8.7). The transition rules and the "one driver per job" rule are enforced again by a trigger
// in the migration, because they are what keeps two drivers from sharing a delivery.

public sealed class DeliveryJobConfiguration : IEntityTypeConfiguration<DeliveryJob>
{
    public void Configure(EntityTypeBuilder<DeliveryJob> builder)
    {
        builder.ToTable("delivery_jobs", table =>
        {
            table.HasCheckConstraint("ck_delivery_job_status", $"status IN ({SnakeCaseEnum<DeliveryJobStatus>.SqlList})");

            // Open means nobody has it; a taken or finished delivery always says who.
            table.HasCheckConstraint(
                "ck_delivery_job_driver",
                "(status = 'open' AND driver_user_id IS NULL) "
                + "OR (status IN ('accepted', 'picked_up', 'delivered') AND driver_user_id IS NOT NULL) "
                + "OR status = 'cancelled'");

            // Each step has its time.
            table.HasCheckConstraint(
                "ck_delivery_job_times",
                "(status <> 'open' OR accepted_at IS NULL) "
                + "AND (status NOT IN ('accepted', 'picked_up', 'delivered') OR accepted_at IS NOT NULL) "
                + "AND (status NOT IN ('picked_up', 'delivered') OR picked_up_at IS NOT NULL) "
                + "AND (status <> 'delivered' OR delivered_at IS NOT NULL) "
                + "AND (status <> 'cancelled' OR cancelled_at IS NOT NULL)");

            // The driver's position exists only while they hold the job, complete, and on Earth.
            table.HasCheckConstraint(
                "ck_delivery_job_location_only_while_active",
                "status IN ('accepted', 'picked_up') OR (last_latitude IS NULL AND last_longitude IS NULL AND last_location_at IS NULL)");
            table.HasCheckConstraint(
                "ck_delivery_job_location_complete",
                "(last_latitude IS NULL) = (last_longitude IS NULL) AND (last_latitude IS NULL) = (last_location_at IS NULL)");
            table.HasCheckConstraint(
                "ck_delivery_job_location_range",
                "(last_latitude IS NULL OR last_latitude BETWEEN -90 AND 90) AND (last_longitude IS NULL OR last_longitude BETWEEN -180 AND 180)");
        });

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Status).HasConversion(SnakeCaseEnum<DeliveryJobStatus>.Converter).HasMaxLength(16);
        builder.Property(j => j.CancelReason).HasMaxLength(DeliveryJob.MaxReasonLength);
        builder.Property(j => j.Version).IsRowVersion();

        builder.HasOne<FoodOrder>().WithMany().HasForeignKey(j => j.FoodOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(j => j.DriverUserId).OnDelete(DeleteBehavior.Restrict);

        // One delivery per order, and one active delivery per driver.
        builder.HasIndex(j => j.FoodOrderId).IsUnique().HasDatabaseName("ux_delivery_jobs_order");
        builder.HasIndex(j => j.DriverUserId).IsUnique().HasDatabaseName("ux_delivery_jobs_one_active_per_driver")
            .HasFilter("status IN ('accepted', 'picked_up')");

        // What the drivers' list scans.
        builder.HasIndex(j => new { j.Status, j.OpenedAt }).HasDatabaseName("ix_delivery_jobs_status_opened");
    }
}
