import { ApiError, NetworkError } from '../../../api/apiErrors';
import { authErrorMessageKey } from './errorMessages';

describe('authErrorMessageKey', () => {
  it('maps known server codes to their messages', () => {
    expect(authErrorMessageKey(new ApiError(400, 'invalid_code'))).toBe('auth.errors.invalid_code');
    expect(authErrorMessageKey(new ApiError(429, 'too_many_requests', 30))).toBe(
      'auth.errors.too_many_requests',
    );
  });

  it('maps network failures', () => {
    expect(authErrorMessageKey(new NetworkError('offline'))).toBe('auth.errors.offline');
    expect(authErrorMessageKey(new NetworkError('timeout'))).toBe('auth.errors.timeout');
  });

  it('falls back to a generic message', () => {
    expect(authErrorMessageKey(new ApiError(500, 'http_500'))).toBe('auth.errors.generic');
    expect(authErrorMessageKey(new Error('boom'))).toBe('auth.errors.generic');
  });
});
