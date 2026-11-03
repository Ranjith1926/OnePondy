import { ApiError, NetworkError } from '../../api/apiErrors';
import { reviewErrorMessageKey } from './reviewErrors';

describe('reviewErrorMessageKey', () => {
  it.each([
    'review_not_eligible',
    'review_window_closed',
    'review_already_exists',
    'review_not_editable',
    'review_already_edited',
    'review_edit_window_closed',
    'review_changed',
    'review_transaction_not_found',
    'review_not_found',
    'validation_failed',
  ])('has its own message for %s', (code) => {
    expect(reviewErrorMessageKey(new ApiError(409, code))).toBe(`reviews.errors.${code}`);
  });

  it('tells a person who is offline, or timed out, so', () => {
    expect(reviewErrorMessageKey(new NetworkError('offline'))).toBe('reviews.errors.offline');
    expect(reviewErrorMessageKey(new NetworkError('timeout'))).toBe('reviews.errors.timeout');
  });

  it('falls back to a general message for anything it does not know', () => {
    expect(reviewErrorMessageKey(new ApiError(500, 'http_500'))).toBe('reviews.errors.generic');
    expect(reviewErrorMessageKey(new Error('boom'))).toBe('reviews.errors.generic');
  });
});
