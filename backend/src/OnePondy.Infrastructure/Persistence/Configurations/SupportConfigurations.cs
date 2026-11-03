using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Food;
using OnePondy.Domain.Support;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Support requests (spec Phase 10, step 10.3). A request and its messages belong to the person who wrote it and go with their account; the
// checks keep a ticket's columns consistent with its status, so a resolved ticket always says who resolved it and when.

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        var resolved = SnakeCaseEnum<SupportTicketStatus>.ToDatabase(SupportTicketStatus.Resolved);

        builder.ToTable("support_tickets", table =>
        {
            table.HasCheckConstraint("ck_support_ticket_status", $"status IN ({SnakeCaseEnum<SupportTicketStatus>.SqlList})");
            table.HasCheckConstraint("ck_support_ticket_topic", $"topic IN ({SnakeCaseEnum<SupportTopic>.SqlList})");
            table.HasCheckConstraint("ck_support_ticket_subject", $"char_length(subject) BETWEEN 1 AND {SupportTicket.MaxSubjectLength}");

            // About a booking or an order, never both.
            table.HasCheckConstraint("ck_support_ticket_about", "booking_id IS NULL OR food_order_id IS NULL");

            // Resolved means resolved at a time, by someone; nothing else has either.
            table.HasCheckConstraint(
                "ck_support_ticket_resolved",
                $"((status = '{resolved}') = (resolved_at IS NOT NULL)) AND ((resolved_at IS NULL) = (resolved_by_user_id IS NULL))");

            // Assigned means a person and a time; escalated means a time and a reason.
            table.HasCheckConstraint("ck_support_ticket_assigned", "(assigned_to_user_id IS NULL) = (assigned_at IS NULL)");
            table.HasCheckConstraint(
                "ck_support_ticket_escalated",
                $"((escalated_at IS NULL) = (escalation_reason IS NULL)) AND (escalation_reason IS NULL OR char_length(escalation_reason) <= {SupportTicket.MaxEscalationReasonLength})");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Topic).HasConversion(SnakeCaseEnum<SupportTopic>.Converter).HasMaxLength(16);
        builder.Property(t => t.Subject).HasMaxLength(SupportTicket.MaxSubjectLength).IsRequired();
        builder.Property(t => t.Status).HasConversion(SnakeCaseEnum<SupportTicketStatus>.Converter).HasMaxLength(24);
        builder.Property(t => t.EscalationReason).HasMaxLength(SupportTicket.MaxEscalationReasonLength);
        builder.Property(t => t.Version).IsRowVersion();
        builder.Ignore(t => t.Reference);
        builder.Ignore(t => t.IsEscalated);

        builder.HasMany(t => t.Messages).WithOne().HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The agents' queue reads one status at a time, oldest first; a person reads their own; an agent reads theirs.
        builder.HasIndex(t => new { t.Status, t.LastRequesterMessageAt }).HasDatabaseName("ix_support_tickets_status_waiting");
        builder.HasIndex(t => new { t.RequesterUserId, t.UpdatedAt }).HasDatabaseName("ix_support_tickets_requester");
        builder.HasIndex(t => new { t.AssignedToUserId, t.Status }).HasDatabaseName("ix_support_tickets_assigned");

        // Tickets are removed with their author's account (the service deletes them first); nothing else cascades.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Business>().WithMany().HasForeignKey(t => t.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Booking>().WithMany().HasForeignKey(t => t.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FoodOrder>().WithMany().HasForeignKey(t => t.FoodOrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> builder)
    {
        builder.ToTable("support_messages", table =>
        {
            table.HasCheckConstraint("ck_support_message_body", $"char_length(body) BETWEEN 1 AND {SupportTicket.MaxBodyLength}");
            table.HasCheckConstraint("ck_support_message_author_kind", $"author_kind IN ({SnakeCaseEnum<SupportAuthorKind>.SqlList})");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.AuthorKind).HasConversion(SnakeCaseEnum<SupportAuthorKind>.Converter).HasMaxLength(16);
        builder.Property(m => m.Body).HasMaxLength(SupportTicket.MaxBodyLength).IsRequired();

        builder.HasIndex(m => new { m.TicketId, m.CreatedAt }).HasDatabaseName("ix_support_messages_ticket_time");

        // Messages cannot outlive the account that wrote them, but a staff member's account is anonymised, never deleted.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
