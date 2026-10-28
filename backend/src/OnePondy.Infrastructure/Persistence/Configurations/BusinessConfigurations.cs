using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Application.Businesses;
using OnePondy.Domain.Businesses;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Tables follow the reference schema in spec §17.3 (Migration 2). The PostGIS "location" generated
// column from that schema is deferred, the same way Place's was (plain latitude/longitude for now;
// see PlaceCatalogService's in-memory distance sort).

public sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        builder.ToTable("businesses", table =>
        {
            table.HasCheckConstraint("ck_business_type", $"business_type IN ({SnakeCaseEnum<BusinessType>.SqlList})");
            table.HasCheckConstraint(
                "ck_business_approval_status",
                $"approval_status IN ({SnakeCaseEnum<BusinessApprovalStatus>.SqlList})");
            table.HasCheckConstraint("ck_business_coordinates", "(latitude IS NULL) = (longitude IS NULL)");
            table.HasCheckConstraint("ck_business_latitude", "latitude IS NULL OR latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_business_longitude", "longitude IS NULL OR longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint("ck_business_active_requires_approval", "NOT is_active OR approval_status = 'approved'");
            table.HasCheckConstraint(
                "ck_business_restaurant_fssai",
                "business_type <> 'restaurant' OR approval_status <> 'approved' OR fssai_license_number IS NOT NULL");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Name).HasMaxLength(BusinessInputValidator.MaxNameLength).IsRequired();
        builder.Property(b => b.BusinessType).HasConversion(SnakeCaseEnum<BusinessType>.Converter).HasMaxLength(24);
        builder.Property(b => b.Description).HasMaxLength(BusinessInputValidator.MaxDescriptionLength);
        builder.Property(b => b.PhoneNumber).HasMaxLength(BusinessInputValidator.MaxPhoneLength);
        builder.Property(b => b.Email).HasMaxLength(BusinessInputValidator.MaxEmailLength);
        builder.Property(b => b.Address).HasMaxLength(BusinessInputValidator.MaxAddressLength);
        builder.Property(b => b.Gstin).HasMaxLength(BusinessInputValidator.MaxGstinLength);
        builder.Property(b => b.FssaiLicenseNumber).HasMaxLength(BusinessInputValidator.MaxFssaiLength);
        builder.Property(b => b.ApprovalStatus).HasConversion(SnakeCaseEnum<BusinessApprovalStatus>.Converter).HasMaxLength(24);
        builder.Property(b => b.ReviewNote).HasMaxLength(BusinessService.MaxReasonLength);
        builder.Property(b => b.Version).IsRowVersion();

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(b => new { b.BusinessType, b.ApprovalStatus, b.IsActive }).HasDatabaseName("ix_businesses_type_status");
        builder.HasIndex(b => b.OwnerUserId);

        builder.OwnsMany(b => b.Documents, document =>
        {
            document.ToTable("business_documents", table => table.HasCheckConstraint(
                "ck_business_document_kind",
                $"kind IN ({SnakeCaseEnum<BusinessDocumentKind>.SqlList})"));
            document.WithOwner().HasForeignKey("BusinessId");
            document.HasKey(d => d.Id);
            document.Property(d => d.Id).ValueGeneratedNever();
            document.Property(d => d.Kind).HasConversion(SnakeCaseEnum<BusinessDocumentKind>.Converter).HasMaxLength(24);
            document.Property(d => d.StorageKey).HasMaxLength(200).IsRequired();
            document.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        });
        builder.Navigation(b => b.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
