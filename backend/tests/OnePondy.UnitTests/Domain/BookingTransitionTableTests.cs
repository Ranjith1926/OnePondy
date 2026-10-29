using OnePondy.Domain.Bookings;
using OnePondy.Domain.Listings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Domain;

/// <summary>
/// The booking state machine against the allowed-transitions table in spec section 7. The server must
/// accept exactly those moves and reject every other.
/// </summary>
public class BookingTransitionTableTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid Partner = Guid.NewGuid();
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    // Spec section 7, "Allowed transitions". failed, expired, refunded and completed are final.
    private static readonly HashSet<(BookingStatus From, BookingStatus To)> Allowed =
    [
        (BookingStatus.Pending, BookingStatus.AwaitingPayment),
        (BookingStatus.Pending, BookingStatus.Confirmed),
        (BookingStatus.Pending, BookingStatus.Rejected),
        (BookingStatus.Pending, BookingStatus.Expired),
        (BookingStatus.Pending, BookingStatus.Cancelled),
        (BookingStatus.AwaitingPayment, BookingStatus.Confirmed),
        (BookingStatus.AwaitingPayment, BookingStatus.Failed),
        (BookingStatus.AwaitingPayment, BookingStatus.Expired),
        (BookingStatus.AwaitingPayment, BookingStatus.Rejected),
        (BookingStatus.AwaitingPayment, BookingStatus.Cancelled),
        (BookingStatus.Confirmed, BookingStatus.Cancelled),
        (BookingStatus.Confirmed, BookingStatus.Completed),
        (BookingStatus.Rejected, BookingStatus.RefundPending),
        (BookingStatus.Cancelled, BookingStatus.RefundPending),
        (BookingStatus.RefundPending, BookingStatus.Refunded),
    ];

    private static Booking Hold() => Booking.CreateHold(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        ListingType.Activity,
        "idem-table",
        Now,
        Now.AddHours(2),
        2,
        new PriceSnapshot(500m, 2, 1000m, 0m, 0m, 0m, 1000m, "INR"),
        new CancellationPolicy(24, 50, null),
        Now);

    /// <summary>A booking that has reached the given status by the moves the spec allows.</summary>
    private static Booking At(BookingStatus status)
    {
        var booking = Hold();
        switch (status)
        {
            case BookingStatus.Pending:
                break;
            case BookingStatus.AwaitingPayment:
                booking.AcceptForPayment(Partner, Now, Window);
                break;
            case BookingStatus.Confirmed:
                booking.Confirm(Partner, Now);
                break;
            case BookingStatus.Failed:
                booking.AcceptForPayment(Partner, Now, Window);
                booking.Fail("declined", Now);
                break;
            case BookingStatus.Expired:
                booking.Expire(Now);
                break;
            case BookingStatus.Rejected:
                booking.Reject(Partner, "no", Now);
                break;
            case BookingStatus.Cancelled:
                booking.Cancel(BookingActorType.Customer, Guid.NewGuid(), null, Now);
                break;
            case BookingStatus.RefundPending:
                booking.Cancel(BookingActorType.Customer, Guid.NewGuid(), null, Now);
                booking.RequestRefund(BookingActorType.System, null, Now);
                break;
            case BookingStatus.Refunded:
                booking.Cancel(BookingActorType.Customer, Guid.NewGuid(), null, Now);
                booking.RequestRefund(BookingActorType.System, null, Now);
                booking.MarkRefunded(Now);
                break;
            case BookingStatus.Completed:
                booking.Confirm(Partner, Now);
                booking.Complete(Partner, BookingActorType.Partner, Now);
                break;
        }

        Assert.Equal(status, booking.Status);
        return booking;
    }

    /// <summary>Every way the domain offers to move a booking, grouped by the status it leads to.</summary>
    private static readonly (BookingStatus To, Action<Booking>[] Moves)[] Moves =
    [
        (BookingStatus.AwaitingPayment, [b => b.AcceptForPayment(Partner, Now, Window)]),
        (BookingStatus.Confirmed, [b => b.Confirm(Partner, Now), b => b.ConfirmPayment(Now)]),
        (BookingStatus.Failed, [b => b.Fail("declined", Now)]),
        (BookingStatus.Expired, [b => b.Expire(Now)]),
        (BookingStatus.Rejected, [b => b.Reject(Partner, "no", Now)]),
        (BookingStatus.Cancelled, [b => b.Cancel(BookingActorType.Customer, Guid.NewGuid(), null, Now)]),
        (BookingStatus.RefundPending, [b => b.RequestRefund(BookingActorType.System, null, Now)]),
        (BookingStatus.Refunded, [b => b.MarkRefunded(Now)]),
        (BookingStatus.Completed, [b => b.Complete(Partner, BookingActorType.Partner, Now)]),
    ];

    [Fact]
    public void The_domain_accepts_exactly_the_transitions_in_the_spec_table()
    {
        var mismatches = new List<string>();

        foreach (var from in Enum.GetValues<BookingStatus>())
        {
            foreach (var (to, moves) in Moves)
            {
                var worked = moves.Any(move =>
                {
                    var booking = At(from);
                    try
                    {
                        move(booking);
                        return booking.Status == to;
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                });

                if (worked != Allowed.Contains((from, to)))
                {
                    mismatches.Add($"{from} -> {to}: domain {(worked ? "allows" : "refuses")}, spec {(worked ? "refuses" : "allows")}");
                }
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void A_partner_confirms_only_a_pending_booking_and_a_payment_confirms_only_one_awaiting_payment()
    {
        Assert.Throws<InvalidOperationException>(() => At(BookingStatus.AwaitingPayment).Confirm(Partner, Now));
        Assert.Throws<InvalidOperationException>(() => At(BookingStatus.Pending).ConfirmPayment(Now));

        var paid = At(BookingStatus.AwaitingPayment);
        paid.ConfirmPayment(Now.AddMinutes(5));

        Assert.Equal(BookingStatus.Confirmed, paid.Status);
        Assert.Null(paid.ExpiresAt);
        Assert.Equal(BookingActorType.System, paid.History[^1].ActorType);
        Assert.Null(paid.History[^1].ActorUserId);
    }

    [Fact]
    public void Accepting_for_payment_sets_the_payment_deadline_and_records_who_accepted()
    {
        var booking = Hold();

        booking.AcceptForPayment(Partner, Now.AddMinutes(20), Window);

        Assert.True(booking.IsHolding);
        Assert.Equal(Now.AddMinutes(50), booking.ExpiresAt);
        Assert.Equal(BookingActorType.Partner, booking.History[^1].ActorType);
        Assert.Equal(Partner, booking.History[^1].ActorUserId);
    }

    [Fact]
    public void A_payment_window_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Hold().AcceptForPayment(Partner, Now, TimeSpan.Zero));
    }

    [Fact]
    public void A_failed_or_expired_booking_has_no_hold_left()
    {
        Assert.Null(At(BookingStatus.Failed).ExpiresAt);
        Assert.Null(At(BookingStatus.Expired).ExpiresAt);
        Assert.False(At(BookingStatus.Failed).IsHolding);
    }
}
