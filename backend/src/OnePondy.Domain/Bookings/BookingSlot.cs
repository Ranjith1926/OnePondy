namespace OnePondy.Domain.Bookings;

/// <summary>
/// Links a booking to the slot(s) it reserves (spec §17.3): a stay reserves one slot per night, an
/// activity or event reserves one slot with a guest-count quantity.
/// </summary>
public sealed class BookingSlot
{
    private BookingSlot()
    {
    }

    public Guid BookingId { get; private set; }

    public Guid SlotId { get; private set; }

    public int Quantity { get; private set; }

    public static BookingSlot Create(Guid bookingId, Guid slotId, int quantity)
    {
        if (quantity < 1)
        {
            throw new ArgumentException("Quantity must be at least 1.", nameof(quantity));
        }

        return new BookingSlot { BookingId = bookingId, SlotId = slotId, Quantity = quantity };
    }
}
