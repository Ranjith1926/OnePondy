import { ApiError, NetworkError } from '../../api/apiErrors';
import { bookingErrorMessageKey } from './bookingErrors';

describe('bookingErrorMessageKey', () => {
  it.each([
    'booking_changed',
    'booking_invalid_transition',
    'booking_not_found',
    'refunds_not_enabled',
    'payment_not_due',
    'payment_window_closed',
    'payments_unavailable',
    'not_enough_availability',
  ])('has its own message for %s', (code) => {
    expect(bookingErrorMessageKey(new ApiError(409, code))).toBe(`bookings.errors.${code}`);
  });

  it('tells offline from a timeout', () => {
    expect(bookingErrorMessageKey(new NetworkError('offline'))).toBe('bookings.errors.offline');
    expect(bookingErrorMessageKey(new NetworkError('timeout'))).toBe('bookings.errors.timeout');
  });

  it.each([new ApiError(500, 'something_new'), new Error('x'), 'text', null])(
    'uses the generic message for %p',
    (error) => {
      expect(bookingErrorMessageKey(error)).toBe('bookings.errors.generic');
    },
  );
});
