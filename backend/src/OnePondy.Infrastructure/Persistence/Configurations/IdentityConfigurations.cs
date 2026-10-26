using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("app_users", table =>
        {
            table.HasCheckConstraint("ck_app_users_language", "preferred_language IN ('en', 'ta', 'fr')");
            table.HasCheckConstraint(
                "ck_app_users_contact",
                "deleted_at IS NOT NULL OR email IS NOT NULL OR phone_number IS NOT NULL");
            table.HasCheckConstraint(
                "ck_app_users_phone_e164",
                @"phone_number IS NULL OR phone_number ~ '^\+[1-9][0-9]{7,14}$'");
        });

        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.PreferredLanguage).HasMaxLength(8).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(32);
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.NormalizedEmail).HasMaxLength(320);

        builder.HasIndex(u => u.PhoneNumber)
            .IsUnique()
            .HasFilter("phone_number IS NOT NULL")
            .HasDatabaseName("ux_app_users_phone");

        // Identity creates a non-unique email index; one account per verified email address.
        builder.HasIndex(u => u.NormalizedEmail)
            .IsUnique()
            .HasFilter("normalized_email IS NOT NULL")
            .HasDatabaseName("ux_app_users_normalized_email");
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.ToTable("app_roles");
        builder.HasData(new IdentityRole<Guid>
        {
            Id = new Guid("0193f0c4-6a5e-7b52-9c1d-3f2a1b0c0001"),
            Name = RoleNames.Customer,
            NormalizedName = RoleNames.Customer.ToUpperInvariant(),
            ConcurrencyStamp = "3c9f9a0e-5d1b-4f44-9a52-6d1f0c2b7e01",
        });
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder) => builder.ToTable("app_user_roles");
}

public sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("app_user_claims");
}

public sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("app_user_logins");
}

public sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("app_user_tokens");
}

public sealed class RoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder) => builder.ToTable("app_role_claims");
}
