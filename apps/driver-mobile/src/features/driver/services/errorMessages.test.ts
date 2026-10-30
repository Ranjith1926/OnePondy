import { ApiError, NetworkError } from '../../../api/apiErrors';
import { driverErrorMessageKey } from './errorMessages';

describe('driverErrorMessageKey', () => {
  it('maps the API codes a driver can meet to their own messages', () => {
    expect(driverErrorMessageKey(new ApiError(409, 'driver_changed'))).toBe(
      'driver.errors.driver_changed',
    );
    expect(driverErrorMessageKey(new ApiError(409, 'driver_locked'))).toBe(
      'driver.errors.driver_locked',
    );
    expect(driverErrorMessageKey(new ApiError(409, 'driver_licence_in_use'))).toBe(
      'driver.errors.driver_licence_in_use',
    );
    expect(driverErrorMessageKey(new ApiError(409, 'driver_not_approved'))).toBe(
      'driver.errors.driver_not_approved',
    );
  });

  it('separates being offline from taking too long', () => {
    expect(driverErrorMessageKey(new NetworkError('offline'))).toBe('driver.errors.offline');
    expect(driverErrorMessageKey(new NetworkError('timeout'))).toBe('driver.errors.timeout');
  });

  it('says something went wrong for anything else, and never shows a raw code', () => {
    expect(driverErrorMessageKey(new ApiError(500, 'http_500'))).toBe('driver.errors.generic');
    expect(driverErrorMessageKey(new Error('boom'))).toBe('driver.errors.generic');
  });
});
