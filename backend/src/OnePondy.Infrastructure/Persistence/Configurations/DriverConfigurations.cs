using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Delivery;
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
