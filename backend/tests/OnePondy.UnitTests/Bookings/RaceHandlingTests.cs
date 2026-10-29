using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>
/// A payment or refund that loses a race with the same event on another request must answer "busy" (409) and change
/// nothing, however the database reports the loss: as a plain failed save, or as a deadlock victim, which EF wraps in an
/// <see cref="InvalidOperationException"/> ("likely due to a transient failure").
/// </summary>
public class RaceHandlingTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    public static TheoryData<string> LostRaces => ["failed save", "deadlock victim"];

    private static Exception LostRace(string kind) => kind == "failed save"
        ? new DbUpdateException("duplicate key")
        : new InvalidOperationException("likely due to a transient failure", new DbUpdateException("deadlock detected"));

    private async Task<(Guid BookingId, string OrderId)> AwaitingWithOrderAsync()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        return (booking.Id, started.OrderId);
    }

    [Theory]
    [MemberData(nameof(LostRaces))]
    public async Task A_payment_that_loses_a_race_answers_busy_and_is_applied_by_the_retry(string kind)
    {
        var (bookingId, orderId) = await AwaitingWithOrderAsync();
        _h.Bookings.Db.FailNextSaveWith = LostRace(kind);

        var raced = await _h.Payments.RecordCaptureAsync(orderId, "pay_race", 1000m, None);
        var retried = await _h.Payments.RecordCaptureAsync(orderId, "pay_race", 1000m, None);

        Assert.Equal(PaymentErrors.Busy.Code, raced.Error!.Code);
        Assert.True(retried.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, (await _h.BookingAsync(bookingId)).Status);
        Assert.Equal(3, (await _h.LedgerAsync()).Count);
    }

    [Theory]
    [MemberData(nameof(LostRaces))]
    public async Task A_refund_that_loses_a_race_answers_busy_and_is_applied_by_the_retry(string kind)
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        await _h.Bookings.Bookings.CancelForBusinessAsync(_h.Bookings.BusinessId, booking.Id, booking.Version, _h.Bookings.Owner, null, None);
        _h.Bookings.Db.FailNextSaveWith = LostRace(kind);

        var raced = await _h.Payments.RecordRefundProcessedAsync("rfnd_test_1", None);
        var retried = await _h.Payments.RecordRefundProcessedAsync("rfnd_test_1", None);

        Assert.Equal(PaymentErrors.Busy.Code, raced.Error!.Code);
        Assert.True(retried.IsSuccess);
        Assert.Equal(BookingStatus.Refunded, (await _h.BookingAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task A_genuine_fault_is_not_mistaken_for_a_lost_race()
    {
        var (_, orderId) = await AwaitingWithOrderAsync();
        _h.Bookings.Db.FailNextSaveWith = new InvalidOperationException("a real bug");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _h.Payments.RecordCaptureAsync(orderId, "pay_bug", 1000m, None));
    }
}
