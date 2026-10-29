import { bookingRefetchInterval } from './useBookings';

describe('bookingRefetchInterval', () => {
  it('re-reads quickly while a booking waits for payment, because the webhook may land at any moment', () => {
    expect(bookingRefetchInterval('awaitingPayment')).toBe(5_000);
  });

  it('re-reads slowly while a refund is on its way', () => {
    expect(bookingRefetchInterval('refundPending')).toBe(15_000);
  });

  it.each(['pending', 'confirmed', 'cancelled', 'refunded', 'expired', undefined])(
    'does not poll a booking that is %s',
    (status) => {
      expect(bookingRefetchInterval(status)).toBe(false);
    },
  );
});
