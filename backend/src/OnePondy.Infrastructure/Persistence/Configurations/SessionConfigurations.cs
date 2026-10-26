using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Channel).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Purpose).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Destination).HasMaxLength(320);
        builder.Property(c => c.CodeHash).IsRequired();

        // Per-destination rate checks and the daily send count.
        builder.HasIndex(c => new { c.Destination, c.CreatedAt });
        builder.HasIndex(c => c.CreatedAt);
    }
}

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DeviceName).HasMaxLength(120);
        builder.Property(s => s.RevokedReason).HasConversion<string>().HasMaxLength(32);

        // Accounts are anonymised, never hard-deleted, so sessions never cascade from users.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.UserId);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.HasOne<UserSession>()
            .WithMany()
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Optimistic concurrency on PostgreSQL's xmin, so two refreshes cannot both rotate one token.
        builder.Property<uint>("Version").IsRowVersion();
    }
}
