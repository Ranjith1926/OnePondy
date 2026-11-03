using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Rides;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Drivers in ride mode (spec Phase 9). The rules the model states are also CHECK constraints, so no code path can break them.

public sealed class RideDriverProfileConfiguration : IEntityTypeConfiguration<RideDriverProfile>
{
    public void Configure(EntityTypeBuilder<RideDriverProfile> builder)
    {
        builder.ToTable("ride_driver_profiles", table =>
        {
            table.HasCheckConstraint("ck_ride_driver_vehicle_class", $"vehicle_class IN ({SnakeCaseEnum<RideVehicleClass>.SqlList})");
            table.HasCheckConstraint("ck_ride_driver_approval_status", $"approval_status IN ({SnakeCaseEnum<DriverApprovalStatus>.SqlList})");

            // Only an approved driver is ever on duty, and "on duty since" exists exactly while on duty.
            table.HasCheckConstraint("ck_ride_driver_duty_requires_approval", "NOT is_on_duty OR approval_status = 'approved'");
            table.HasCheckConstraint("ck_ride_driver_duty_since", "is_on_duty = (on_duty_since IS NOT NULL)");

            // A rejection or suspension always says why.
            table.HasCheckConstraint("ck_ride_driver_review_note", "approval_status NOT IN ('rejected', 'suspended') OR review_note IS NOT NULL");

            // The position exists only while on duty, is complete, and is on Earth: it is never kept after a shift.
            table.HasCheckConstraint(
                "ck_ride_driver_location_only_on_duty",
                "is_on_duty OR (last_latitude IS NULL AND last_longitude IS NULL AND last_location_at IS NULL)");
            table.HasCheckConstraint(
                "ck_ride_driver_location_complete",
                "(last_latitude IS NULL) = (last_longitude IS NULL) AND (last_latitude IS NULL) = (last_location_at IS NULL)");
            table.HasCheckConstraint(
                "ck_ride_driver_location_range",
                "(last_latitude IS NULL OR last_latitude BETWEEN -90 AND 90) AND (last_longitude IS NULL OR last_longitude BETWEEN -180 AND 180)");
        });

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).ValueGeneratedNever();
        builder.Property(p => p.FullName).HasMaxLength(RideDriverProfile.MaxNameLength).IsRequired();
        builder.Property(p => p.VehicleClass).HasConversion(SnakeCaseEnum<RideVehicleClass>.Converter).HasMaxLength(16);
        builder.Property(p => p.VehicleNumber).HasMaxLength(RideDriverProfile.MaxVehicleNumberLength).IsRequired();
        builder.Property(p => p.LicenceNumber).HasMaxLength(RideDriverProfile.MaxLicenceLength).IsRequired();
        builder.Property(p => p.PermitNumber).HasMaxLength(RideDriverProfile.MaxPermitLength).IsRequired();
        builder.Property(p => p.InsurancePolicyNumber).HasMaxLength(RideDriverProfile.MaxPolicyLength).IsRequired();
        builder.Property(p => p.ApprovalStatus).HasConversion(SnakeCaseEnum<DriverApprovalStatus>.Converter).HasMaxLength(16);
        builder.Property(p => p.ReviewNote).HasMaxLength(RideDriverProfile.MaxReasonLength);
        builder.Property(p => p.Version).IsRowVersion();

        // The account is anonymised, never deleted, so this never cascades: deleting an account removes its profile explicitly.
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<RideDriverProfile>(p => p.UserId).OnDelete(DeleteBehavior.Restrict);

        // One licence and one vehicle, one account: the same person or vehicle cannot hold several ride accounts.
        builder.HasIndex(p => p.LicenceNumber).IsUnique().HasDatabaseName("ux_ride_driver_profiles_licence");
        builder.HasIndex(p => p.VehicleNumber).IsUnique().HasDatabaseName("ux_ride_driver_profiles_vehicle");

        // What dispatch and the review queue scan.
        builder.HasIndex(p => new { p.ApprovalStatus, p.IsOnDuty }).HasDatabaseName("ix_ride_driver_profiles_status_duty");
    }
}
